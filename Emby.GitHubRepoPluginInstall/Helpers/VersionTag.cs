using System;
using System.Text.RegularExpressions;

namespace Emby.GitHubRepoPluginInstall.Helpers;

public static class VersionTag
{
    private static readonly Regex Leading = new Regex(@"^\D*(\d+(?:\.\d+){0,3})", RegexOptions.Compiled);

    /// <summary>Reads a version out of a release tag ("v1.0.1.837-b4cb64e" -> 1.0.1.837); <c>null</c> if there is none.</summary>
    public static Version Parse(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var match = Leading.Match(tag.Trim());
        return match.Success && Version.TryParse(PadParts(match.Groups[1].Value), out var version) ? version : null;
    }

    /// <summary>Fills missing parts with 0 so 1.0.0 and 1.0.0.0 compare equal.</summary>
    public static Version Normalize(Version version)
    {
        if (version == null) return null;
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
    }

    private static string PadParts(string value)
    {
        var parts = value.Split('.').Length;
        if (parts == 1) value += ".0";
        for (var i = Math.Max(parts, 2); i < 4; i++) value += ".0";
        return value;
    }
}
