using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emby.GitHubRepoPluginInstall.Models;

namespace Emby.GitHubRepoPluginInstall.Services;

public static class InstalledPluginMatcher
{
    /// <summary>Finds the loaded plugin a repo installs: by assembly name, then by DLL file name.</summary>
    public static InstalledPlugin Find(ReposToProcess repo, IEnumerable<InstalledPlugin> installed)
    {
        var plugins = installed?.ToList() ?? new List<InstalledPlugin>();
        if (repo == null || plugins.Count == 0) return null;

        return FindByAssembly(repo.AssemblyName, plugins) ??
               FindByFile(repo.LatestDllName, plugins) ??
               FindByFile(repo.FileName, plugins);
    }

    public static InstalledPlugin FindByAssembly(string assemblyName, IEnumerable<InstalledPlugin> installed)
    {
        if (string.IsNullOrEmpty(assemblyName)) return null;

        return installed?.FirstOrDefault(p => string.Equals(p.AssemblyName, assemblyName, StringComparison.OrdinalIgnoreCase)) ??
               installed?.FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p.FilePath ?? ""), assemblyName, StringComparison.OrdinalIgnoreCase));
    }

    private static InstalledPlugin FindByFile(string dllName, IEnumerable<InstalledPlugin> installed)
    {
        if (string.IsNullOrEmpty(dllName)) return null;

        var baseName = Path.GetFileNameWithoutExtension(dllName);
        return installed.FirstOrDefault(p => string.Equals(p.AssemblyName, baseName, StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(p.FileName, dllName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Fills each repo's <see cref="ReposToProcess.InstalledVersion"/> and matched plugin from what Emby has loaded.</summary>
    public static void Apply(IEnumerable<ReposToProcess> repos, IReadOnlyCollection<InstalledPlugin> installed)
    {
        foreach (var repo in repos ?? Enumerable.Empty<ReposToProcess>())
            repo.SetInstalled(Find(repo, installed));
    }
}
