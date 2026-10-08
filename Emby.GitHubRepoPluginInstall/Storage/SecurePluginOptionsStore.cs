using System;
using Emby.GitHubRepoPluginInstall.Models;
using Emby.GitHubRepoPluginInstall.Security;
using Emby.GitHubRepoPluginInstall.UIBaseClasses.Store;
using MediaBrowser.Common;
using MediaBrowser.Model.Logging;

namespace Emby.GitHubRepoPluginInstall.Storage;

public class SecurePluginOptionsStore : PluginOptionsStore
{
    private readonly ISecureStorage _secureStorage;
    
    public SecurePluginOptionsStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName) 
        : base(applicationHost, logger, pluginFullName)
    {
        _secureStorage = new SecureStorage(logger);
        
        // Subscribe to events to handle encryption/decryption
        this.FileSaving += OnFileSaving;
    }
    
    public override PluginUIOptions GetOptions()
    {
        var options = base.GetOptions();
        
        // Always decrypt the token when loading for UI display
        if (!string.IsNullOrEmpty(options.EncryptedGitHubToken))
        {
            options.GitHubToken = _secureStorage.Unprotect(options.EncryptedGitHubToken);
        }
        
        return options;
    }
    
    // Saving clears GitHubToken on the in-memory options, so callers holding those options read it through here
    public string GetGitHubToken(PluginUIOptions options)
    {
        if (!string.IsNullOrEmpty(options.GitHubToken))
            return options.GitHubToken;

        return string.IsNullOrEmpty(options.EncryptedGitHubToken) ? null : _secureStorage.Unprotect(options.EncryptedGitHubToken);
    }

    private void OnFileSaving(object sender, FileSavingEventArgs e)
    {
        if (e.Options is PluginUIOptions options)
        {
            // Encrypt the token before saving
            if (!string.IsNullOrEmpty(options.GitHubToken))
            {
                options.EncryptedGitHubToken = _secureStorage.Protect(options.GitHubToken);
                // Clear the plain text token so it's not kept in memory
                options.GitHubToken = null;
            }
        }
    }
}