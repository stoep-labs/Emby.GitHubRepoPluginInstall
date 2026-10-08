using System;
using System.ComponentModel;
using System.Linq;
using Emby.GitHubRepoPluginInstall.GithubAPI;
using Emby.GitHubRepoPluginInstall.Helpers;

namespace Emby.GitHubRepoPluginInstall.Models;

public class ReposToProcess
{
    public ReposToProcess()
    {
        Id = Guid.NewGuid()
                 .ToString("N");
    }

    public string Id            { get; set; }
    public string Url           { get; set; }
    public bool   GetPreRelease { get; set; }
    public bool   AutoUpdate    { get; set; } = true;

    [ReadOnly(true)]
    public DateTime? LastDateTimeChecked { get; set; }

    public string LastVersionDownloaded { get; set; }
    public string FileName              { get; set; }

    // Latest release info, persisted so the UI can render without calling GitHub
    public string          LatestVersion       { get; set; }
    public DateTimeOffset? LatestPublishedAt   { get; set; }
    public bool            LatestIsPreRelease  { get; set; }
    public bool            LatestHasDll        { get; set; }
    public string          LatestReleaseNotes  { get; set; }

    public string LatestDllName { get; set; }

    // Assembly name of the DLL this repo installs, learnt on the first install
    public string AssemblyName { get; set; }

    // What Emby actually has loaded for this repo, refreshed from the running server before display/checks
    public string InstalledVersion  { get; set; }
    public string InstalledName     { get; set; }
    public string InstalledFileName { get; set; }

    public void SetInstalled(InstalledPlugin plugin)
    {
        InstalledVersion  = plugin?.Version == null ? null : VersionTag.Normalize(plugin.Version).ToString();
        InstalledName     = plugin?.Name;
        InstalledFileName = plugin?.FileName;
    }

    private Version LatestParsed    => VersionTag.Parse(LatestVersion);
    private Version InstalledParsed => VersionTag.Parse(InstalledVersion);
    private bool    Comparable      => LatestParsed != null && InstalledParsed != null;

    public bool InstalledIsNewer => Comparable && InstalledParsed > LatestParsed;

    public DateTime? LastInstalledUtc { get; set; }

    // When this server process started; an install from before then should already be loaded
    public static DateTime ServerStartedUtc { get; set; } = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();

    // Downloaded since the server started, but Emby still runs the old DLL until it restarts
    public bool RestartPending =>
        Comparable && InstalledParsed < LatestParsed &&
        string.Equals(LatestVersion, LastVersionDownloaded, StringComparison.OrdinalIgnoreCase) &&
        LastInstalledUtc > ServerStartedUtc;

    private bool IsCurrent =>
        Comparable
            ? InstalledParsed == LatestParsed
            : string.Equals(LatestVersion, LastVersionDownloaded, StringComparison.OrdinalIgnoreCase);

    public bool UpdateAvailable =>
        LatestHasDll && !string.IsNullOrEmpty(LatestVersion) && !IsCurrent && !InstalledIsNewer && !RestartPending;

    // A loaded copy of this plugin under another file name that installing would replace
    public bool ReplacesOtherFile =>
        !string.IsNullOrEmpty(InstalledFileName) && !string.IsNullOrEmpty(LatestDllName) &&
        !InstalledFileName.Equals(LatestDllName, StringComparison.OrdinalIgnoreCase);

    public string Status
    {
        get
        {
            if (string.IsNullOrEmpty(LatestVersion)) return "Not checked";
            if (InstalledIsNewer) return "Installed version is newer";
            if (RestartPending) return "Restart pending";
            if (UpdateAvailable) return "Update available";
            if (IsCurrent) return "Up to date";
            return "No DLL in latest release";
        }
    }

    public void ApplyLatestRelease(GitHubRelease release)
    {
        LastDateTimeChecked = DateTime.UtcNow;
        if (release == null) return;

        LatestVersion      = release.TagName;
        LatestPublishedAt  = release.PublishedAt;
        LatestIsPreRelease = release.PreRelease;
        LatestHasDll       = release.Assets?.Any(x => x.IsDll) == true;
        LatestDllName      = release.Assets?.FirstOrDefault(x => x.IsDll)?.Name;
        LatestReleaseNotes = string.IsNullOrWhiteSpace(release.Body) ? release.Name : release.Body;
    }

    public void ClearLatestRelease()
    {
        LastDateTimeChecked = null;
        LatestVersion      = null;
        LatestPublishedAt  = null;
        LatestIsPreRelease = false;
        LatestHasDll       = false;
        LatestDllName      = null;
        LatestReleaseNotes = null;
    }

    private (string Owner, string Repository) RepositoryInfo
    {
        get
        {
            if (string.IsNullOrEmpty(Url)) return (string.Empty, string.Empty);

            try
            {
                // Split the URL by '/' and get owner and repo parts
                var parts = Url.TrimEnd('/')
                               .Split('/');
                if (parts.Length >= 5)
                    return (parts[3], parts[4]
                        .Split('/')[0]);
            }
            catch
            {
                // Return empty if parsing fails
                return (string.Empty, string.Empty);
            }

            return (string.Empty, string.Empty);
        }
    }

    public string Owner      => RepositoryInfo.Owner;
    public string Repository => RepositoryInfo.Repository;
}
