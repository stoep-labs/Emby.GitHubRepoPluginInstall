using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.DxGrid;
using Emby.Web.GenericEdit.Elements.List;
using MediaBrowser.Model.Attributes;

namespace Emby.GitHubRepoPluginInstall.Models;

public class PluginUIOptions : EditableOptionsBase
{
    [DontSave]
    public override string EditorTitle => "Download Plugsin From GitHub";

    // Emby caps plugin pages at 720px, which squeezes the grids; the style lifts the cap only on
    // the container holding this marker so other plugin pages keep the default layout
    internal const string WideLayoutStyle =
        "<style id=\"ghrpi-wide\">" +
        ".readOnlyContent:has(#ghrpi-wide),.readOnlyContent:has(#ghrpi-wide) form{max-width:none}" +
        "</style>";

    // Emby 4.10's generic plugin page throws while setting the title (genericui.js getTitle reads
    // item.Caption before the page data arrives), which aborts the menu highlight. This hidden image's
    // onerror registers one document-wide viewshow hook that highlights the menu entry matching the
    // current /genericui route; it does nothing when the right entry is already highlighted
    internal const string MenuHighlightFix =
        "<img src=\"data:,\" alt=\"\" style=\"display:none\" onerror=\"" +
        "if(!window.__ghrpiNav){window.__ghrpiNav=1;" +
        "var fix=function(){try{" +
        "var cur=decodeURIComponent(location.hash.split('#!')[1]||'').toLowerCase();" +
        "if(cur.indexOf('/genericui')!==0)return;" +
        "var seen=[],opts=document.querySelectorAll('.navMenuOption');" +
        "for(var i=0;i<opts.length;i++){var c=opts[i].parentElement;" +
        "while(c&&!(c.items&&c.getElement))c=c.parentElement;" +
        "if(!c||seen.indexOf(c)>=0)continue;seen.push(c);" +
        "for(var j=0;j<c.items.length;j++){var it=c.items[j],id=it.navMenuId||(it.href?'/'+it.href:'');" +
        "if(decodeURIComponent(id).toLowerCase()!==cur)continue;" +
        "var el=c.getElement(j);if(!el||el.classList.contains('navMenuOption-selected'))return;" +
        "var old=document.querySelectorAll('.navMenuOption.navMenuOption-selected');" +
        "for(var k=0;k<old.length;k++)old[k].classList.remove('navMenuOption-selected');" +
        "el.classList.add('navMenuOption-selected');return;}}" +
        "}catch(e){}};" +
        "var run=function(){[0,250,1000].forEach(function(t){setTimeout(fix,t);});};" +
        "document.addEventListener('viewshow',run);run();}" +
        "\">";

    [DontSave]
    public override string EditorDescription =>
        "This plugin allows you to download and install plugins from GitHub repositories." + WideLayoutStyle + MenuHighlightFix;

    [DontSave]
    public CaptionItem UpdatesBanner { get; set; } = new CaptionItem("")
                                                     {
                                                         IsVisible = false
                                                     };

    [DontSave]
    public CaptionItem Logs { get; set; } = new CaptionItem("")
                                            {
                                                IsVisible = false
                                            };

    [DontSave]
    public CaptionItem RepoLogs { get; set; } = new CaptionItem("")
                                               {
                                                   IsVisible = false
                                               };


    [DontSave]
    public CaptionItem CaptionBasic { get; set; } = new CaptionItem("Basic Settings");

    [DisplayName("GitHub Token (PAT)")]
    [Description("Generate a GitHub Personal Access Token (PAT) and enter it here.")]
    [Required]
    [DontSave]
    public string GitHubToken { get; set; }
    
    [Browsable(false)]
    public string EncryptedGitHubToken { get; set; }

    public bool RestartServerAfterInstall { get; set; }

    [DontSave]
    [VisibleCondition(nameof(GitHubToken), SimpleCondition.IsNotNullOrEmpty)]
    public ButtonItem SaveTokenBtn =>
        new ButtonItem
        {
            Icon    = IconNames.save_alt,
            Caption = "Save Settings",
            Data1   = "Save"
        };


    [DontSave]
    public ButtonItem Add =>
        new ButtonItem
        {
            Icon    = IconNames.add,
            Caption = "Add Repository",
            Data1   = "Add"
        };

    [Browsable(false)]
    [DontSave]
    public IList<string> SelectedItemId { get; set; }


    [Browsable(false)]
    public List<ReposToProcess> Repos { get; set; } = new List<ReposToProcess>();

    [Browsable(false)]
    public List<PluginRegistry> PluginRegistries { get; set; } = new List<PluginRegistry>();

    [DisplayName("Current Repositories")]
    [DontSave]
    [GridDataSource(nameof(Repos))]
    [GridSelectionSource(nameof(SelectedItemId))]
    public DxDataGrid Grid
    {
        get
        {
            var options = new DxGridOptions(new ReposToProcess(), "Id", false, true, true, false);

            options.selection.mode         = DxGridSelection.SelectionMode.single;
            options.columnResizingMode     = DxGridOptions.ColumnResizingMode.widget;
            options.stateStoring           = GridStateStoring("repos");
            options.heightMode             = DxGridOptions.GridHeightMode.medium;
            options.allowColumnReordering  = true;
            options.grouping.autoExpandAll = true;
            options.focusedRowEnabled      = true;

            foreach (var column in options.columns) column.visible = false;

            var repo = options.columns.FirstOrDefault(e => e.dataField == nameof(ReposToProcess.Repository));
            if (repo != null)
            {
                repo.caption      = "Repository";
                repo.width        = 200;
                repo.visibleIndex = 7;
                repo.visible      = true;
            }

            var owner = options.columns.FirstOrDefault(e => e.dataField == nameof(ReposToProcess.Owner));
            if (owner != null)
            {
                owner.caption      = "Owner";
                owner.width        = 150;
                owner.visibleIndex = 7;
                owner.groupIndex   = 0;
                owner.visible      = true;
            }

            var status = options.columns.FirstOrDefault(e => e.dataField == nameof(ReposToProcess.Status));
            if (status != null)
            {
                status.caption      = "Status";
                status.width        = 150;
                status.visibleIndex = 8;
                status.visible      = true;
            }

            var lastVersionDownloaded =
                options.columns.FirstOrDefault(e => e.dataField == nameof(ReposToProcess.LastVersionDownloaded));
            if (lastVersionDownloaded != null)
            {
                lastVersionDownloaded.caption      = "Last Version Downloaded";
                lastVersionDownloaded.width        = 190;
                lastVersionDownloaded.visibleIndex = 9;
                lastVersionDownloaded.visible      = true;
            }

            var latestVersion =
                options.columns.FirstOrDefault(e => e.dataField == nameof(ReposToProcess.LatestVersion));
            if (latestVersion != null)
            {
                latestVersion.caption      = "Latest Version";
                latestVersion.width        = 190;
                latestVersion.visibleIndex = 10;
                latestVersion.visible      = true;
            }

            var preRelCol =options.columns.FirstOrDefault(e => e.dataField == nameof(ReposToProcess.GetPreRelease));
            if (preRelCol != null)
            {
                preRelCol.caption      = "Get PreRelease";
                preRelCol.width        = 100;
                preRelCol.visibleIndex = 20;
                preRelCol.visible      = true;
            }

            var autoUpdateCol = options.columns.FirstOrDefault(e => e.dataField == nameof(ReposToProcess.AutoUpdate));
            if (autoUpdateCol != null)
            {
                autoUpdateCol.caption      = "Auto Update";
                autoUpdateCol.width        = 100;
                autoUpdateCol.visibleIndex = 30;
                autoUpdateCol.visible      = true;
            }

            var lastDateTimeChecked =
                options.columns.FirstOrDefault(e => e.dataField == nameof(ReposToProcess.LastDateTimeChecked));
            if (lastDateTimeChecked != null)
            {
                lastDateTimeChecked.caption      = "Last Checked";
                lastDateTimeChecked.width        = 200;
                lastDateTimeChecked.dataType     = DxGridColumn.ColumnDataType.datetime;
                lastDateTimeChecked.visibleIndex = 40;
                lastDateTimeChecked.visible      = true;
            }

            var urlCol = options.columns.FirstOrDefault(e => e.dataField == nameof(ReposToProcess.Url));
            if (urlCol != null)
            {
                urlCol.caption      = "Url";
                urlCol.width        = 400;
                urlCol.visibleIndex = 50;
                urlCol.visible      = true;
            }

            return new DxDataGrid(options);
        }
    }

    [DontSave]
    public ButtonItem Delete =>
        new ButtonItem
        {
            Icon               = IconNames.delete_forever,
            Caption            = "Remove Selected",
            Data1              = "Remove",
            ConfirmationPrompt = "Are you sure you want to remove the selected item?"
        };

    [DontSave]
    public ButtonItem Edit =>
        new ButtonItem
        {
            Icon    = IconNames.edit,
            Caption = "Edit Selected",
            Data1   = "Edit"
        };

    [DontSave]
    public ButtonItem UpdateAll =>
        new ButtonItem
        {
            Icon    = IconNames.system_update_alt,
            Caption = "Update All Plugins",
            Data1   = "UpdateAll",
            ConfirmationPrompt = "Are you sure you want to update all plugins that have available updates?"
        };

    [DontSave]
    public ButtonItem CheckAllForUpdates =>
        new ButtonItem
        {
            Icon    = IconNames.refresh,
            Caption = "Check All for Updates",
            Data1   = "CheckAll"
        };


    [DontSave]
    public CaptionItem CaptionRegistries { get; set; } = new CaptionItem("Plugin Registries");

    [DontSave]
    public ButtonItem AddRegistry =>
        new ButtonItem
        {
            Icon    = IconNames.library_add,
            Caption = "Add Registry",
            Data1   = "AddRegistry"
        };

    [Browsable(false)]
    [DontSave]
    public IList<string> SelectedRegistryId { get; set; }

    [DisplayName("Current Registries")]
    [DontSave]
    [GridDataSource(nameof(PluginRegistries))]
    [GridSelectionSource(nameof(SelectedRegistryId))]
    public DxDataGrid RegistryGrid
    {
        get
        {
            var options = new DxGridOptions(new PluginRegistry(), "Id", false, true, true, false);

            options.selection.mode         = DxGridSelection.SelectionMode.single;
            options.columnResizingMode     = DxGridOptions.ColumnResizingMode.widget;
            options.stateStoring           = GridStateStoring("registries");
            options.heightMode             = DxGridOptions.GridHeightMode.small;
            options.allowColumnReordering  = true;
            options.focusedRowEnabled      = true;

            foreach (var column in options.columns) column.visible = false;

            var nameCol = options.columns.FirstOrDefault(e => e.dataField == nameof(PluginRegistry.Name));
            if (nameCol != null)
            {
                nameCol.caption      = "Registry Name";
                nameCol.width        = 300;
                nameCol.visibleIndex = 1;
                nameCol.visible      = true;
            }

            var urlCol = options.columns.FirstOrDefault(e => e.dataField == nameof(PluginRegistry.RawUrl));
            if (urlCol != null)
            {
                urlCol.caption      = "URL";
                urlCol.width        = 500;
                urlCol.visibleIndex = 2;
                urlCol.visible      = true;
            }

            var enabledCol = options.columns.FirstOrDefault(e => e.dataField == nameof(PluginRegistry.Enabled));
            if (enabledCol != null)
            {
                enabledCol.caption      = "Enabled";
                enabledCol.width        = 100;
                enabledCol.visibleIndex = 3;
                enabledCol.visible      = true;
            }

            return new DxDataGrid(options);
        }
    }

    [DontSave]
    public ButtonItem DeleteRegistry =>
        new ButtonItem
        {
            Icon               = IconNames.delete_forever,
            Caption            = "Remove Registry",
            Data1              = "RemoveRegistry",
            ConfirmationPrompt = "Are you sure you want to remove the selected registry?"
        };

    [DontSave]
    public ButtonItem EditRegistry =>
        new ButtonItem
        {
            Icon    = IconNames.edit,
            Caption = "Edit Registry",
            Data1   = "EditRegistry"
        };

    [DontSave]
    public CaptionItem RegistryLogs { get; set; } = new CaptionItem("")
                                                   {
                                                       IsVisible = false
                                                   };


    [DontSave]
    public CaptionItem CaptionLatestReleases { get; set; } = new CaptionItem("Latest Releases");

    [DisplayName("Releases")]
    [DontSave]
    public GenericItemList Releases { get; set; }

    // DevExtreme keeps column widths, order and sorting in the browser's localStorage under this key
    private static object GridStateStoring(string grid) =>
        new Dictionary<string, object>
        {
            ["enabled"]    = true,
            ["type"]       = "localStorage",
            ["storageKey"] = "GitHubRepoPluginInstall." + grid + "Grid"
        };

    /// <summary>Points a self-update entry that still tracks the legacy upstream repo at the current one.</summary>
    /// <returns><c>true</c> if an entry was changed and the options need saving.</returns>
    public bool MigrateLegacySelfRepo()
    {
        var changed = false;
        foreach (var repo in Repos ?? new List<ReposToProcess>())
        {
            if (!string.Equals(repo.Url?.Trim().TrimEnd('/'), GitHubRepPluginInstall.LegacyRepositoryUrl, System.StringComparison.OrdinalIgnoreCase))
                continue;

            repo.Url = GitHubRepPluginInstall.RepositoryUrl;
            repo.ClearLatestRelease();
            changed = true;
        }

        return changed;
    }
}