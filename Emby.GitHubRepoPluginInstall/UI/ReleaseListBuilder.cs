using System;
using System.Collections.Generic;
using System.Linq;
using Emby.GitHubRepoPluginInstall.Models;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.List;

namespace Emby.GitHubRepoPluginInstall.UI;

/// <summary>Builds the release list purely from persisted repo state - never calls GitHub.</summary>
public static class ReleaseListBuilder
{
    public static GenericItemList Build(IEnumerable<ReposToProcess> repos)
    {
        var list = new GenericItemList();
        if (repos == null) return list;

        // Updates first so they can't be missed
        foreach (var repo in repos.OrderByDescending(r => r.UpdateAvailable))
        {
            if (string.IsNullOrEmpty(repo.LatestVersion))
            {
                list.Add(new GenericListItem
                         {
                             PrimaryText   = "Repo: " + repo.Repository,
                             SecondaryText = "Not checked yet - click \"Check All for Updates\"",
                             Icon          = IconNames.help_outline,
                             IconMode      = ItemListIconMode.LargeRegular
                         });
                continue;
            }

            var note = repo.LatestReleaseNotes?.Replace("\n", "<br/>") ?? "No release notes available";

            var item = new GenericListItem
                       {
                           PrimaryText = "Repo: " + repo.Repository + " - " + repo.Status,
                           SecondaryText = "Installed: "                   +
                                           (repo.LastVersionDownloaded ?? "none") +
                                           Environment.NewLine              +
                                           "Latest: "                       +
                                           repo.LatestVersion               +
                                           (repo.LatestIsPreRelease ? " (pre-release)" : ""),
                           Icon     = repo.UpdateAvailable ? IconNames.new_releases : IconNames.check_circle_outline,
                           IconMode = ItemListIconMode.LargeRegular,
                           SubItems = new GenericItemList
                                      {
                                          new GenericListItem
                                          {
                                              PrimaryText   = "Release Notes:<br/>" + note,
                                              SecondaryText = "Updated At: " + repo.LatestPublishedAt,
                                              Icon          = IconNames.message,
                                              IconMode      = ItemListIconMode.LargeRegular
                                          }
                                      }
                       };

            if (repo.LatestHasDll)
                item.Button1 = new ButtonItem
                               {
                                   Caption = repo.UpdateAvailable ? "Install Update" : "Reinstall",
                                   Data1   = "Download",
                                   Data2   = repo.Id
                               };

            list.Add(item);
        }

        return list;
    }

    /// <summary>One-line banner text when any repo has an update, otherwise <c>null</c>.</summary>
    public static string BuildSummary(IEnumerable<ReposToProcess> repos)
    {
        var updates = repos?.Where(r => r.UpdateAvailable).ToList() ?? new List<ReposToProcess>();
        if (updates.Count == 0) return null;

        var names = string.Join(", ", updates.Select(r => $"{r.Repository} ({r.LastVersionDownloaded ?? "none"} -> {r.LatestVersion})"));
        return $"{updates.Count} update{(updates.Count == 1 ? "" : "s")} available: {names}. Click \"Update All Plugins\" to install.";
    }
}
