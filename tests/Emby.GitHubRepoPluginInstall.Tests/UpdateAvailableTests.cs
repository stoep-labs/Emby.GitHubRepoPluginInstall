using System.Collections.Generic;
using System.Linq;
using Emby.GitHubRepoPluginInstall.Models;
using Emby.GitHubRepoPluginInstall.UI;
using Emby.Web.GenericEdit.Elements;
using Xunit;

namespace Emby.GitHubRepoPluginInstall.Tests;

public class UpdateAvailableTests
{
    private static ReposToProcess Repo(string name, string installed, string latest, bool hasDll = true) =>
        new ReposToProcess
        {
            Url                   = "https://github.com/owner/" + name,
            LastVersionDownloaded = installed,
            LatestVersion         = latest,
            LatestHasDll          = hasDll
        };

    [Theory]
    [InlineData("v1.0", "v1.1", true,  true,  "Update available")]
    [InlineData("v1.1", "V1.1", true,  false, "Up to date")]
    [InlineData(null,   "v1.1", true,  true,  "Update available")]
    [InlineData("v1.0", "v1.1", false, false, "No DLL in latest release")]
    [InlineData("v1.0", null,   true,  false, "Not checked")]
    public void Status_ComparesInstalledWithLatest(string installed, string latest, bool hasDll, bool expectedUpdate, string expectedStatus)
    {
        var repo = Repo("x", installed, latest, hasDll);

        Assert.Equal(expectedUpdate, repo.UpdateAvailable);
        Assert.Equal(expectedStatus, repo.Status);
    }

    [Fact]
    public void Build_ListsUpdatesFirstAndFlagsThem()
    {
        var repos = new List<ReposToProcess>
                    {
                        Repo("current", "v2", "v2"),
                        Repo("stale", "v1", "v3")
                    };

        var items = ReleaseListBuilder.Build(repos).ToList();

        Assert.Contains("stale", items[0].PrimaryText);
        Assert.Contains("Update available", items[0].PrimaryText);
        Assert.Contains("v1", items[0].SecondaryText);
        Assert.Contains("v3", items[0].SecondaryText);
        Assert.Equal(IconNames.new_releases, items[0].Icon);
        Assert.Equal("Install Update", items[0].Button1?.Caption);

        Assert.Contains("current", items[1].PrimaryText);
        Assert.Equal(IconNames.check_circle_outline, items[1].Icon);
    }

    [Fact]
    public void BuildSummary_NamesReposWithUpdates()
    {
        Assert.Null(ReleaseListBuilder.BuildSummary(new[] { Repo("current", "v2", "v2") }));

        var summary = ReleaseListBuilder.BuildSummary(new[] { Repo("a", "v1", "v2"), Repo("b", "v1", "v2"), Repo("c", "v2", "v2") });

        Assert.StartsWith("2 updates available", summary);
        Assert.Contains("a", summary);
        Assert.Contains("b", summary);
        Assert.DoesNotContain("c (", summary);
    }
}
