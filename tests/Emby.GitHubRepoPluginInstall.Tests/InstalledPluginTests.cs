using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Emby.GitHubRepoPluginInstall.GithubAPI;
using Emby.GitHubRepoPluginInstall.Helpers;
using Emby.GitHubRepoPluginInstall.Models;
using Emby.GitHubRepoPluginInstall.Services;
using Emby.GitHubRepoPluginInstall.UI;
using MediaBrowser.Model.Logging;
using NSubstitute;
using Xunit;

namespace Emby.GitHubRepoPluginInstall.Tests;

public class InstalledPluginTests
{
    [Theory]
    [InlineData("v4.2.0.27", "4.2.0.27")]
    [InlineData("v1.0.1.837-b4cb64e", "1.0.1.837")]
    [InlineData("v1.0.0", "1.0.0.0")]
    [InlineData("v2", "2.0.0.0")]
    [InlineData("2026.10.8.9", "2026.10.8.9")]
    [InlineData("release-candidate", null)]
    [InlineData(null, null)]
    public void VersionTag_ParsesLeadingVersion(string tag, string expected)
    {
        Assert.Equal(expected, VersionTag.Parse(tag)?.ToString());
    }

    private static ReposToProcess Repo(string installed, string latest, string downloaded = null) =>
        new ReposToProcess
        {
            LastInstalledUtc      = downloaded == null ? null : DateTime.UtcNow,
            Url                   = "https://github.com/owner/plugin",
            InstalledVersion      = installed,
            LatestVersion         = latest,
            LastVersionDownloaded = downloaded,
            LatestHasDll          = true,
            LatestDllName         = "Plugin.dll"
        };

    [Fact]
    public void Status_RetriesAnInstallThatAnEarlierRestartDidNotLoad()
    {
        var repo = Repo("1.0.0.0", "v1.1", "v1.1");

        repo.LastInstalledUtc = ReposToProcess.ServerStartedUtc.AddMinutes(-5);
        Assert.True(repo.UpdateAvailable);

        repo.LastInstalledUtc = ReposToProcess.ServerStartedUtc.AddMinutes(5);
        Assert.Equal("Restart pending", repo.Status);
    }

    [Theory]
    [InlineData("4.2.0.77", "v4.2.0.27", null,   false, "Installed version is newer")]
    [InlineData("1.0.0.0",  "v1.1",      "v1.1", false, "Restart pending")]
    [InlineData("1.0.0.0",  "v1.1",      null,   true,  "Update available")]
    [InlineData("1.0.0.0",  "v1.0.0",    null,   false, "Up to date")]
    [InlineData(null,       "v1.0.0",    null,   true,  "Update available")]
    public void Status_UsesTheVersionEmbyHasLoaded(string installed, string latest, string downloaded, bool update, string status)
    {
        var repo = Repo(installed, latest, downloaded);

        Assert.Equal(update, repo.UpdateAvailable);
        Assert.Equal(status, repo.Status);
    }

    [Fact]
    public void Matcher_PrefersAssemblyNameThenDllName()
    {
        var installed = new List<InstalledPlugin>
                        {
                            new InstalledPlugin { Name = "Other", AssemblyName = "Other", FilePath = "/p/Other.dll", Version = new Version(1, 0) },
                            new InstalledPlugin { Name = "HSC", AssemblyName = "HomeScreenCompanion", FilePath = "/p/HomeScreenCompanion.dll", Version = new Version(4, 2, 0, 77) }
                        };

        Assert.Equal("HSC", InstalledPluginMatcher.Find(new ReposToProcess { AssemblyName = "homescreencompanion" }, installed)?.Name);
        Assert.Equal("HSC", InstalledPluginMatcher.Find(new ReposToProcess { LatestDllName = "HomeScreenCompanion.dll" }, installed)?.Name);
        Assert.Null(InstalledPluginMatcher.Find(new ReposToProcess { LatestDllName = "Missing.dll" }, installed));

        var repo = new ReposToProcess { LatestDllName = "HomeScreenCompanion.dll" };
        InstalledPluginMatcher.Apply(new[] { repo }, installed);
        Assert.Equal("4.2.0.77", repo.InstalledVersion);
    }

    [Fact]
    public void ReleaseList_OffersDowngradeOnlyBehindAConfirmation()
    {
        var newer = Repo("4.2.0.77", "v4.2.0.27");
        var renamed = Repo("1.0.0.0", "v1.1");
        renamed.InstalledFileName = "Plugin.Old.dll";
        renamed.InstalledName     = "Plugin";

        var items = ReleaseListBuilder.Build(new[] { newer, renamed }).ToList();
        var downgrade = items.Single(i => i.Button1?.Data2 == newer.Id).Button1;
        var replace   = items.Single(i => i.Button1?.Data2 == renamed.Id).Button1;

        Assert.Equal("Install Older Version", downgrade.Caption);
        Assert.Equal("Downgrade", downgrade.Data1);
        Assert.Contains("4.2.0.77", downgrade.ConfirmationPrompt);
        Assert.Equal("Download", replace.Data1);
        Assert.Contains("Plugin.Old.dll", replace.ConfirmationPrompt);
        Assert.Null(ReleaseListBuilder.BuildSummary(new[] { newer }));
    }

    private sealed class Sandbox : IDisposable
    {
        public readonly string Dir = Path.Combine(Path.GetTempPath(), "ghrpi-" + Guid.NewGuid().ToString("N"));
        public readonly AssemblyName Dll = AssemblyName.GetAssemblyName(typeof(ReposToProcess).Assembly.Location);

        public Sandbox() => Directory.CreateDirectory(Dir);

        public string Temp()
        {
            var path = Path.Combine(Dir, "New.dll.temp");
            File.Copy(typeof(ReposToProcess).Assembly.Location, path);
            return path;
        }

        public void Dispose() => Directory.Delete(Dir, true);
    }

    private static async Task<InstallResult> Install(Sandbox box, InstalledPlugin existing, ReposToProcess repo, bool allowDowngrade)
    {
        var temp   = box.Temp();
        var client = Substitute.For<IGitHubApiClient>();
        client.DownloadReleaseToTempAsync(Arg.Any<GitHubRelease>(), box.Dir, Arg.Any<CancellationToken>())
              .Returns(Task.FromResult((temp, "New.dll")));

        return await ReleaseInstaller.InstallAsync(client, new GitHubRelease { TagName = "v1.0.0" }, repo, box.Dir,
                                                   existing == null ? new List<InstalledPlugin>() : new List<InstalledPlugin> { existing },
                                                   allowDowngrade, Substitute.For<ILogger>());
    }

    [Fact]
    public async Task Install_RefusesToDowngradeANewerInstalledPlugin()
    {
        using var box = new Sandbox();
        var existing = new InstalledPlugin { Name = "Plugin", AssemblyName = box.Dll.Name, Version = new Version(box.Dll.Version.Major + 1, 0), FilePath = Path.Combine(box.Dir, "New.dll") };
        var repo = new ReposToProcess();

        var result = await Install(box, existing, repo, allowDowngrade: false);

        Assert.True(result.SkippedNewerInstalled);
        Assert.False(result.Installed);
        Assert.Empty(Directory.GetFiles(box.Dir));
        Assert.Null(repo.LastVersionDownloaded);
    }

    [Fact]
    public async Task Install_DowngradesWhenAllowed()
    {
        using var box = new Sandbox();
        var existing = new InstalledPlugin { Name = "Plugin", AssemblyName = box.Dll.Name, Version = new Version(box.Dll.Version.Major + 1, 0), FilePath = Path.Combine(box.Dir, "New.dll") };

        var result = await Install(box, existing, new ReposToProcess(), allowDowngrade: true);

        Assert.True(result.Installed);
        Assert.True(File.Exists(Path.Combine(box.Dir, "New.dll")));
    }

    [Fact]
    public async Task Install_RemovesTheSamePluginUnderAnotherFileName()
    {
        using var box = new Sandbox();
        var oldPath = Path.Combine(box.Dir, "Old.dll");
        File.WriteAllText(oldPath, "old copy");
        var existing = new InstalledPlugin { Name = "Plugin", AssemblyName = box.Dll.Name, Version = new Version(0, 0, 1), FilePath = oldPath };
        var repo = new ReposToProcess();

        var result = await Install(box, existing, repo, allowDowngrade: false);

        Assert.True(result.Installed);
        Assert.Equal(oldPath, result.ReplacedFile);
        Assert.False(File.Exists(oldPath));
        Assert.Equal(new[] { "New.dll" }, Directory.GetFiles(box.Dir).Select(Path.GetFileName));
        Assert.Equal(box.Dll.Name, repo.AssemblyName);
        Assert.Equal("v1.0.0", repo.LastVersionDownloaded);
        Assert.NotNull(repo.LastInstalledUtc);
    }
}
