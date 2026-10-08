using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Common;

namespace Emby.GitHubRepoPluginInstall.Models;

/// <summary>A plugin Emby currently has loaded, whatever installed it (catalog, this plugin, or by hand).</summary>
public sealed class InstalledPlugin
{
    public string  Name         { get; set; }
    public string  AssemblyName { get; set; }
    public Version Version      { get; set; }
    public string  FilePath     { get; set; }

    public string FileName => string.IsNullOrEmpty(FilePath) ? null : Path.GetFileName(FilePath);

    public static List<InstalledPlugin> Snapshot(IApplicationHost appHost)
    {
        return (appHost?.Plugins ?? Array.Empty<MediaBrowser.Common.Plugins.IPlugin>())
               .Select(p => new InstalledPlugin
                            {
                                Name         = p.Name,
                                AssemblyName = p.GetType().Assembly.GetName().Name,
                                Version      = p.Version,
                                FilePath     = p.AssemblyFilePath
                            })
               .ToList();
    }
}
