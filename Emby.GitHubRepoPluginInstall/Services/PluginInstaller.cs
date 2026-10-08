using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Emby.GitHubRepoPluginInstall.Helpers;
using Emby.GitHubRepoPluginInstall.Models;

namespace Emby.GitHubRepoPluginInstall.Services;

/// <summary>A downloaded plugin DLL waiting in a temp file, compared against what Emby has loaded.</summary>
public sealed class PendingInstall
{
    public string          TempPath     { get; set; }
    public string          TargetPath   { get; set; }
    public string          AssemblyName { get; set; }
    public Version         Version      { get; set; }
    public InstalledPlugin Existing     { get; set; }

    public string FileName => Path.GetFileName(TargetPath);

    public bool IsDowngrade =>
        Existing?.Version != null && Version != null &&
        VersionTag.Normalize(Existing.Version) > VersionTag.Normalize(Version);

    public bool ReplacesOtherFile =>
        !string.IsNullOrEmpty(Existing?.FilePath) &&
        !string.Equals(Path.GetFullPath(Existing.FilePath), Path.GetFullPath(TargetPath), StringComparison.OrdinalIgnoreCase);
}

public static class PluginInstaller
{
    /// <summary>Reads the downloaded DLL's identity without loading it and finds the copy Emby already has.</summary>
    public static PendingInstall Inspect(string tempPath, string targetPath, IEnumerable<InstalledPlugin> installed)
    {
        var name = AssemblyName.GetAssemblyName(tempPath);

        return new PendingInstall
               {
                   TempPath     = tempPath,
                   TargetPath   = targetPath,
                   AssemblyName = name.Name,
                   Version      = name.Version,
                   Existing     = InstalledPluginMatcher.FindByAssembly(name.Name, installed)
               };
    }

    /// <summary>Moves the download into place and deletes a copy of the same plugin under another file name.</summary>
    /// <returns>The path of the replaced file, or <c>null</c>.</returns>
    public static string Commit(PendingInstall pending)
    {
        if (File.Exists(pending.TargetPath)) File.Delete(pending.TargetPath);
        File.Move(pending.TempPath, pending.TargetPath);

        if (!pending.ReplacesOtherFile || !File.Exists(pending.Existing.FilePath)) return null;

        File.Delete(pending.Existing.FilePath);
        return pending.Existing.FilePath;
    }

    public static void Discard(PendingInstall pending) => DeleteQuietly(pending?.TempPath);

    public static void DeleteQuietly(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort; a leftover .temp file is never loaded by Emby
        }
    }
}
