using System;
using System.Collections.Generic;
using System.Linq;
using Emby.GitHubRepoPluginInstall.Models;
using Emby.GitHubRepoPluginInstall.UI;
using Xunit;

namespace Emby.GitHubRepoPluginInstall.Tests;

public class ReleaseListBuilderTests
{
    [Fact]
    public void Build_RendersOneItemPerRepoFromPersistedState()
    {
        var repos = new List<ReposToProcess>
                    {
                        new ReposToProcess
                        {
                            Url                = "https://github.com/owner/checked",
                            LatestVersion      = "v1.2.3",
                            LatestPublishedAt  = DateTimeOffset.UtcNow,
                            LatestHasDll       = true,
                            LatestReleaseNotes = "line1\nline2"
                        },
                        new ReposToProcess { Url = "https://github.com/owner/unchecked" }
                    };

        var items = ReleaseListBuilder.Build(repos).ToList();

        Assert.Equal(2, items.Count);
        Assert.Contains("v1.2.3", items[0].SecondaryText);
        Assert.Equal("Download", items[0].Button1?.Data1);
        Assert.Equal(repos[0].Id, items[0].Button1?.Data2);
        Assert.Null(items[1].Button1);
        Assert.Contains("Not checked yet", items[1].SecondaryText);
    }
}
