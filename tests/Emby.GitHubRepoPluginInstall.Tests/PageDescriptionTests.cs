using System.Text.RegularExpressions;
using Emby.GitHubRepoPluginInstall.Models;
using Xunit;

namespace Emby.GitHubRepoPluginInstall.Tests;

public class PageDescriptionTests
{
    [Fact]
    public void Description_CarriesWideLayoutStyleAndMenuHighlightFix()
    {
        var description = new PluginUIOptions().EditorDescription;

        Assert.Contains("<style id=\"ghrpi-wide\">", description);
        Assert.Contains("window.__ghrpiNav", description);

        // The script sits in a double-quoted onerror attribute, so it must not contain a double quote itself
        var onerror = Regex.Match(description, "onerror=\"([^\"]*)\">");
        Assert.True(onerror.Success);
        Assert.EndsWith("run();}", onerror.Groups[1].Value);
    }
}
