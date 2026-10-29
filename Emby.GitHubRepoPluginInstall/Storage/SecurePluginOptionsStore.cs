using System;
using System.IO;
using Emby.GitHubRepoPluginInstall.Models;
using Emby.GitHubRepoPluginInstall.Security;
using Emby.GitHubRepoPluginInstall.UIBaseClasses.Store;
using MediaBrowser.Common;
using MediaBrowser.Model.Logging;

namespace Emby.GitHubRepoPluginInstall.Storage;

public class SecurePluginOptionsStore : PluginOptionsStore
{
    private readonly ILogger        _logger;
    private readonly ISecureStorage _secureStorage;
    
    public SecurePluginOptionsStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName) 
        : base(applicationHost, logger, pluginFullName)
    {
        _logger        = logger;
        _secureStorage = new SecureStorage(logger, Path.Combine(Path.GetDirectoryName(OptionsFilePath), $"{pluginFullName}.key"));
        
        // Subscribe to events to handle encryption/decryption
        this.FileSaving += OnFileSaving;
    }
    
    public override PluginUIOptions GetOptions()
    {
        var options = base.GetOptions();
        
        // Always decrypt the token when loading for UI display; an unreadable token comes back empty, never as garbage
        if (!string.IsNullOrEmpty(options.EncryptedGitHubToken))
        {
            var token = _secureStorage.Unprotect(options.EncryptedGitHubToken, out var usedLegacyKey);
            options.GitHubToken = string.IsNullOrEmpty(token) ? null : token;

            if (usedLegacyKey)
            {
                // Re-save under the stable key now, before the hostname changes (e.g. the container is recreated)
                SetOptions(options);
                options.GitHubToken = token;
                _logger.Info("GitHub token re-saved with the stable key");
            }
        }
        
        return options;
    }
    
    // Saving clears GitHubToken on the in-memory options, so callers holding those options read it through here
    public string GetGitHubToken(PluginUIOptions options)
    {
        if (!string.IsNullOrEmpty(options.GitHubToken))
            return options.GitHubToken;

        if (string.IsNullOrEmpty(options.EncryptedGitHubToken))
            return null;

        var token = _secureStorage.Unprotect(options.EncryptedGitHubToken);
        return string.IsNullOrEmpty(token) ? null : token;
    }

    private void OnFileSaving(object sender, FileSavingEventArgs e)
    {
        if (e.Options is PluginUIOptions options)
        {
            // Encrypt the token before saving; anything that doesn't look like a token leaves the stored one untouched
            if (!string.IsNullOrEmpty(options.GitHubToken))
            {
                var token = options.GitHubToken.Trim();
                if (SecureStorage.IsPlausibleToken(token))
                    options.EncryptedGitHubToken = _secureStorage.Protect(token);
                else
                    _logger.Warn("The entered GitHub token is not a valid token and was not saved");

                // Clear the plain text token so it's not kept in memory
                options.GitHubToken = null;
            }
        }
    }
}