using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emby.GitHubRepoPluginInstall.GithubAPI;
using Emby.GitHubRepoPluginInstall.Models;
using Emby.GitHubRepoPluginInstall.Services;
using Emby.GitHubRepoPluginInstall.Storage;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;

namespace Emby.GitHubRepoPluginInstall.ScheduledTasks;

public class UpdatePlugins : IScheduledTask, IConfigurableScheduledTask
{
    private readonly IActivityManager _activityManager;
    private readonly IApplicationHost _applicationHost;
    private readonly ILogger          _logger;
    private readonly IUserManager     _userManager;
    private readonly IJsonSerializer  _jsonSerializer;

    public UpdatePlugins(ILogManager logManager,
                         IApplicationHost applicationHost,
                         IUserManager userManager,
                         IActivityManager activityManager,
                         IJsonSerializer jsonSerializer)
    {
        _logger          = logManager.GetLogger("UpdatePluginsScheduledTask");
        _applicationHost = applicationHost;
        _userManager     = userManager;
        _activityManager = activityManager;
        _jsonSerializer  = jsonSerializer;
    }

    public bool IsHidden  => false;
    public bool IsEnabled => true;
    public bool IsLogged  => true;

    public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
    {
        var adminUser        = GetAdminUser();
        var store            = new SecurePluginOptionsStore(_applicationHost, _logger, GitHubRepPluginInstall.PluginName);
        var applicationPaths = _applicationHost.Resolve<IApplicationPaths>();

        var pluginUiOptions = store.GetOptions();
        if (pluginUiOptions.MigrateLegacySelfRepo())
            _logger.Info($"Self-update entry moved to {GitHubRepPluginInstall.RepositoryUrl}");

        var totalCollections = pluginUiOptions.Repos.Count;
        var processedRepos   = 0;
        var downloads        = 0;

        using var gitHubClient = new GitHubApiClient(pluginUiOptions.GitHubToken, _jsonSerializer, _logger);
        var installed = InstalledPlugin.Snapshot(_applicationHost);

        // Refresh release info for every repo (keeps the UI current without it calling GitHub);
        // only AutoUpdate repos are downloaded and logged
        foreach (var repo in pluginUiOptions.Repos)
        {
            try
            {
                var previousLatest = repo.LatestVersion;
                var release = await gitHubClient.GetLatestReleaseAsync(repo, true, cancellationToken).ConfigureAwait(false);
                repo.ApplyLatestRelease(release);
                repo.SetInstalled(InstalledPluginMatcher.Find(repo, installed));

                if (!repo.AutoUpdate)
                {
                    // Announce each new version once; the plugin page shows it until installed
                    if (repo.UpdateAvailable && !repo.LatestVersion.Equals(previousLatest, StringComparison.OrdinalIgnoreCase))
                        _activityManager.Create(new ActivityLogEntry
                                                {
                                                    Name          = $"Update available for {repo.Repository}: {repo.LatestVersion}",
                                                    Overview      = Helpers.ActivityLogHelper.CreateInfoHtml(
                                                        $"Update Available for {repo.Repository}",
                                                        "Auto update is off. Install it from the GitHub Repo Plugin Install page.",
                                                        $"Installed: {repo.LastVersionDownloaded ?? "none"}\nLatest: {repo.LatestVersion}\nRepository: {repo.Owner}/{repo.Repository}"),
                                                    ShortOverview = null,
                                                    Type          = "GithubRepoPluginUpdateAvailable",
                                                    ItemId        = null,
                                                    Date          = DateTimeOffset.Now,
                                                    UserId        = adminUser?.InternalId.ToString(),
                                                    Severity      = LogSeverity.Info
                                                });
                }
                else if (release == null)
                {
                    _activityManager.Create(new ActivityLogEntry
                                            {
                                                Name          = $"Release for {repo.Repository} NOT Updated",
                                                Overview      = Helpers.ActivityLogHelper.CreateWarningHtml(
                                                    $"No Release Found for {repo.Repository}",
                                                    "The repository does not have any releases available.",
                                                    $"Repository: {repo.Owner}/{repo.Repository}\nURL: {repo.Url}\nPre-release enabled: {repo.GetPreRelease}"),
                                                ShortOverview = null,
                                                Type          = "GithubRepoPluginUpdateFailed",
                                                ItemId        = null,
                                                Date          = DateTimeOffset.Now,
                                                UserId        = adminUser?.InternalId.ToString(),
                                                Severity      = LogSeverity.Warn
                                            });
                }
                else if (repo.InstalledIsNewer)
                {
                    _logger.Info($"{repo.Repository}: installed {repo.InstalledVersion} is newer than GitHub {release.TagName}, not downgrading");
                }
                else if (repo.UpdateAvailable)
                {
                    var result = await ReleaseInstaller.InstallAsync(gitHubClient, release, repo, applicationPaths.PluginsPath, installed, false, _logger, cancellationToken)
                                                       .ConfigureAwait(false);
                    // Not installed when the DLL itself is older than what Emby runs; the installer logs that
                    if (result.Installed)
                    {
                        downloads++;
                        LogInstalled(repo, release, result.ReplacedFile);
                    }
                }
                else
                {
                    _logger.Info($"{repo.Repository} is up to date ({repo.LastVersionDownloaded})");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _activityManager.Create(new ActivityLogEntry
                                        {
                                            Name          = $"Authentication Failed for {repo.Repository}",
                                            Overview      = Helpers.ActivityLogHelper.CreateErrorHtml(
                                                $"Authentication Failed for {repo.Repository}",
                                                "GitHub API returned 401 Unauthorized. Please check your Personal Access Token.",
                                                ex.Message,
                                                null),
                                            ShortOverview = null,
                                            Type          = "GithubRepoPluginUpdateFailed",
                                            ItemId        = null,
                                            Date          = DateTimeOffset.Now,
                                            UserId        = adminUser?.InternalId.ToString(),
                                            Severity      = LogSeverity.Error
                                        });
            }
            catch (Exception ex)
            {
                _activityManager.Create(new ActivityLogEntry
                                        {
                                            Name          = $"Release {repo.Repository} NOT Updated",
                                            Overview      = Helpers.ActivityLogHelper.CreateErrorHtml(
                                                $"Failed to Update Plugin {repo.Repository}",
                                                "An error occurred while trying to update the plugin.",
                                                ex.Message,
                                                ex.StackTrace),
                                            ShortOverview = null,
                                            Type          = "GithubRepoPluginUpdateFailed",
                                            ItemId        = null,
                                            Date          = DateTimeOffset.Now,
                                            UserId        = adminUser?.InternalId.ToString(),
                                            Severity      = LogSeverity.Fatal
                                        });
            }

            store.SetOptions(pluginUiOptions);
            processedRepos++;
            var progressPercentage = (double)processedRepos / totalCollections * 100;
            progress.Report(progressPercentage);

            cancellationToken.ThrowIfCancellationRequested();
        }

        if (downloads > 0 && pluginUiOptions.RestartServerAfterInstall)
            _applicationHost.Restart();
        else if (downloads > 0) _applicationHost.NotifyPendingRestart();
    }

    private void LogInstalled(ReposToProcess repo, GitHubRelease release, string replacedFile)
    {
        if (replacedFile != null)
            _activityManager.Create(new ActivityLogEntry
                                    {
                                        Name          = $"Plugin {repo.Repository}: replaced {Path.GetFileName(replacedFile)}",
                                        Overview      = Helpers.ActivityLogHelper.CreateInfoHtml(
                                            $"Replaced {Path.GetFileName(replacedFile)}",
                                            "The same plugin was installed under another file name; the old file was removed so only one copy loads.",
                                            $"Removed: {replacedFile}\nInstalled: {repo.FileName} {repo.LastVersionDownloaded}"),
                                        ShortOverview = null,
                                        Type          = "PluginInstalled",
                                        ItemId        = null,
                                        Date          = DateTimeOffset.Now,
                                        Severity      = LogSeverity.Info
                                    });

        _activityManager.Create(new ActivityLogEntry
                                {
                                    Name = $"Plugin {repo.Repository} updated to {repo.LastVersionDownloaded}",
                                    Overview = Helpers.ActivityLogHelper.CreateSuccessHtml(
                                        $"Plugin {repo.Repository} Successfully Updated",
                                        "Plugin has been downloaded and installed.",
                                        repo.LastVersionDownloaded,
                                        release.Body ?? release.GitHubCommit?.GitHubCommitDetails?.Message ?? "No release notes available."),
                                    ShortOverview = null,
                                    Type          = "PluginInstalled",
                                    ItemId        = null,
                                    Date          = DateTimeOffset.Now,
                                    //UserId        = adminUser?.InternalId.ToString(),
                                    Severity = LogSeverity.Info
                                });
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
               {
                   new TaskTriggerInfo
                   {
                       Type = TaskTriggerInfo.TriggerInterval,
                       IntervalTicks = TimeSpan.FromHours(24)
                                               .Ticks
                   }
               };
    }

    public string Name        { get; } = "Update Plugins From Github Repos";
    public string Key         { get; } = nameof(UpdatePlugins);
    public string Description { get; } = "Checks every repo for new releases and installs those marked auto update.";
    public string Category    { get; } = "Github Repo Plugins Update";

    private User GetAdminUser()
    {
        return _userManager.GetUsers(new UserQuery
                                     {
                                         IsAdministrator = true
                                     })
                           .Items.FirstOrDefault();
    }
}