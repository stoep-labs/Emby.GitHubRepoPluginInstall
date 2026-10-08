using System;
using System.ComponentModel;
using System.Linq;
using Emby.GitHubRepoPluginInstall.GithubAPI;

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

    public void ApplyLatestRelease(GitHubRelease release)
    {
        LastDateTimeChecked = DateTime.UtcNow;
        if (release == null) return;

        LatestVersion      = release.TagName;
        LatestPublishedAt  = release.PublishedAt;
        LatestIsPreRelease = release.PreRelease;
        LatestHasDll       = release.Assets?.Any(x => x.IsDll) == true;
        LatestReleaseNotes = string.IsNullOrWhiteSpace(release.Body) ? release.Name : release.Body;
    }

    public void ClearLatestRelease()
    {
        LastDateTimeChecked = null;
        LatestVersion      = null;
        LatestPublishedAt  = null;
        LatestIsPreRelease = false;
        LatestHasDll       = false;
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
