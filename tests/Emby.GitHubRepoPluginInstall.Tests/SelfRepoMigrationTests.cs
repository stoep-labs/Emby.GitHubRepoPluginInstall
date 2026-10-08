using System.Collections.Generic;
using Emby.GitHubRepoPluginInstall.Models;
using Xunit;

namespace Emby.GitHubRepoPluginInstall.Tests;

public class SelfRepoMigrationTests
{
    [Theory]
    [InlineData("https://github.com/bakes82/Emby.GitHubRepoPluginInstall")]
    [InlineData("https://github.com/Bakes82/Emby.GitHubRepoPluginInstall/")]
    public void MigrateLegacySelfRepo_MovesLegacyEntryToFork(string legacyUrl)
    {
        var self  = new ReposToProcess { Url = legacyUrl, LastVersionDownloaded = "2025.08.01.2", LatestVersion = "2025.08.01.2" };
        var other = new ReposToProcess { Url = "https://github.com/bakes82/Emby.CodecKiller" };
        var options = new PluginUIOptions { Repos = new List<ReposToProcess> { self, other } };

        Assert.True(options.MigrateLegacySelfRepo());

        Assert.Equal(GitHubRepPluginInstall.RepositoryUrl, self.Url);
        Assert.Null(self.LatestVersion);
        Assert.Equal("2025.08.01.2", self.LastVersionDownloaded);
        Assert.Equal("https://github.com/bakes82/Emby.CodecKiller", other.Url);
        Assert.False(options.MigrateLegacySelfRepo());
    }
}
