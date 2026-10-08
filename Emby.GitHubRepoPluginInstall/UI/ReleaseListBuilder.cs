using System;
using System.Collections.Generic;
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

        foreach (var repo in repos)
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
                           PrimaryText = "Repo: " + repo.Repository,
                           SecondaryText = "Version: "         +
                                           repo.LatestVersion  +
                                           Environment.NewLine +
                                           "PreRelease: "      +
                                           repo.LatestIsPreRelease,
                           Icon     = IconNames.download,
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
                                   Caption = "Download",
                                   Data1   = "Download",
                                   Data2   = repo.Id
                               };

            list.Add(item);
        }

        return list;
    }
}
