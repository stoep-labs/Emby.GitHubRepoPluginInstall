using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Emby.GitHubRepoPluginInstall.GithubAPI;
using Emby.GitHubRepoPluginInstall.Models;
using MediaBrowser.Model.Logging;

namespace Emby.GitHubRepoPluginInstall.Services;

public sealed class InstallResult
{
    public bool    Installed            { get; set; }
    public bool    SkippedNewerInstalled { get; set; }
    public Version DownloadedVersion    { get; set; }
    public Version InstalledVersion     { get; set; }
    public string  ReplacedFile         { get; set; }
}

/// <summary>Downloads a release and installs it only if that doesn't downgrade the copy Emby already runs.</summary>
public static class ReleaseInstaller
{
    public static async Task<InstallResult> InstallAsync(IGitHubApiClient client,
                                                         GitHubRelease release,
                                                         ReposToProcess repo,
                                                         string pluginsPath,
                                                         IReadOnlyCollection<InstalledPlugin> installed,
                                                         bool allowDowngrade,
                                                         ILogger logger,
                                                         CancellationToken cancellationToken = default)
    {
        var (tempPath, fileName) = await client.DownloadReleaseToTempAsync(release, pluginsPath, cancellationToken).ConfigureAwait(false);

        try
        {
            var pending = PluginInstaller.Inspect(tempPath, Path.Combine(pluginsPath, fileName), installed);
            var result = new InstallResult
                         {
                             DownloadedVersion = pending.Version,
                             InstalledVersion  = pending.Existing?.Version
                         };

            if (pending.IsDowngrade && !allowDowngrade)
            {
                PluginInstaller.Discard(pending);
                logger.Info($"Skipped {repo.Repository} {release.TagName}: installed {pending.Existing.Name} {pending.Existing.Version} is newer than {pending.Version}");
                result.SkippedNewerInstalled = true;
                return result;
            }

            result.ReplacedFile = PluginInstaller.Commit(pending);
            if (result.ReplacedFile != null)
                logger.Info($"Installed {fileName} for {repo.Repository} and removed {Path.GetFileName(result.ReplacedFile)}, an older copy of the same plugin");

            repo.LastVersionDownloaded = release.TagName;
            repo.FileName              = fileName;
            repo.AssemblyName          = pending.AssemblyName;
            repo.LastInstalledUtc      = DateTime.UtcNow;
            result.Installed           = true;
            return result;
        }
        catch
        {
            PluginInstaller.DeleteQuietly(tempPath);
            throw;
        }
    }
}
