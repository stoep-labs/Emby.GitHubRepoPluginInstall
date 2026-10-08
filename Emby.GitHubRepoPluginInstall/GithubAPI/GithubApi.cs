using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Emby.GitHubRepoPluginInstall.Models;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.GitHubRepoPluginInstall.GithubAPI;

public class GitHubApiClient : IDisposable, IGitHubApiClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    // One client for the process: avoids socket churn and bounds every call with a timeout
    private static readonly HttpClient SharedClient = new HttpClient { Timeout = RequestTimeout };

    private readonly HttpClient      _client;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly ILogger         _logger;
    private readonly string          _githubToken;
    
    // Simple in-memory cache
    private static readonly Dictionary<string, CacheItem> _cache = new Dictionary<string, CacheItem>();
    private static readonly object _cacheLock = new object();
    
    private class CacheItem
    {
        public object Data { get; set; }
        public DateTime ExpiryTime { get; set; }
        
        public bool IsExpired => DateTime.UtcNow > ExpiryTime;
    }

    public GitHubApiClient(string githubToken, HttpClient httpClient, IJsonSerializer jsonSerializer, ILogger logger)
    {
        _jsonSerializer = jsonSerializer;
        _logger         = logger;
        _client         = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _githubToken    = githubToken;
    }

    public GitHubApiClient(string githubToken, IJsonSerializer jsonSerializer, ILogger logger)
    {
        _jsonSerializer = jsonSerializer;
        _logger         = logger;
        _client         = SharedClient;
        _githubToken    = githubToken;

        if (string.IsNullOrEmpty(githubToken))
            _logger.Debug("No GitHub token provided - API requests may be rate limited or fail");
    }

    private HttpRequestMessage CreateRequest(string url, string accept = "application/json")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("GitHubReleaseManager", "1.0"));

        if (!string.IsNullOrEmpty(_githubToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _githubToken);

        return request;
    }

    public async Task<List<GitHubRelease>> GetLatestReleasesAsync(ReposToProcess _repo, CancellationToken cancellationToken = default)
    {
        return await GetLatestReleasesAsync(new List<ReposToProcess>
                                            {
                                                _repo
                                            }, cancellationToken);
    }

    public async Task<GitHubRelease> GetLatestReleaseAsync(ReposToProcess _repo, CancellationToken cancellationToken = default)
    {
        var release = await GetLatestReleasesAsync(new List<ReposToProcess>
                                                   {
                                                       _repo
                                                   }, cancellationToken);
        return release.OrderByDescending(x => x.PublishedAt)
                      .FirstOrDefault();
    }

    public async Task<GitHubRelease> GetLatestReleaseAsync(ReposToProcess _repo, bool bypassCache, CancellationToken cancellationToken = default)
    {
        if (!bypassCache)
            return await GetLatestReleaseAsync(_repo, cancellationToken);
            
        return await GetLatestReleaseForRepoAsync(_repo, cancellationToken, true);
    }

    public async Task<List<GitHubRelease>> GetLatestReleasesAsync(List<ReposToProcess> _repositories, CancellationToken cancellationToken = default)
    {
        var tasks = _repositories.Select(repo => GetLatestReleaseForRepoAsync(repo, cancellationToken)).ToArray();
        var results = await Task.WhenAll(tasks);
        
        return results.Where(r => r != null).ToList();
    }

    private async Task<GitHubRelease> GetLatestReleaseForRepoAsync(ReposToProcess repo, CancellationToken cancellationToken = default, bool bypassCache = false)
    {
        var cacheKey = $"releases_{repo.Owner}_{repo.Repository}_{repo.GetPreRelease}";
        
        // Check cache first (unless bypassed)
        if (!bypassCache && TryGetFromCache<GitHubRelease>(cacheKey, out var cachedRelease))
        {
            _logger.Debug($"Returning cached release for {repo.Owner}/{repo.Repository}");
            return cachedRelease;
        }

        try
        {
            var url = $"https://api.github.com/repos/{repo.Owner}/{repo.Repository}/releases";
            using var response = await ExecuteWithRetryAsync(() => CreateRequest(url), cancellationToken).ConfigureAwait(false);
            
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new UnauthorizedAccessException($"GitHub API returned 401 Unauthorized for {repo.Owner}/{repo.Repository}. Please check your GitHub token is valid and has the necessary permissions.");
            }
            
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.Warn($"Repository {repo.Owner}/{repo.Repository} not found (404). Possible causes: " +
                           "1) Repository doesn't exist, " +
                           "2) Repository is private and your token lacks 'repo' scope, " +
                           "3) Repository name/owner is incorrect.");
                return null;
            }
            
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.Error($"Access forbidden (403) for {repo.Owner}/{repo.Repository}. " +
                            "This usually means your token lacks the required 'repo' scope for private repositories.");
                return null;
            }
            
            response.EnsureSuccessStatusCode();

            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            {
                var gitHubReleases = _jsonSerializer.DeserializeFromStream<List<GitHubRelease>>(stream);
                if (gitHubReleases?.Any() != true)
                    return null;

                var filteredReleases = repo.GetPreRelease 
                    ? gitHubReleases 
                    : gitHubReleases.Where(x => !x.PreRelease).ToList();
                filteredReleases = filteredReleases.Where(x => !x.Draft).ToList();

                var latestRelease = filteredReleases
                    .OrderByDescending(x => x.PublishedAt)
                    .FirstOrDefault();

                if (latestRelease != null)
                {
                    latestRelease.BaseRepoUrl = $"https://api.github.com/repos/{repo.Owner}/{repo.Repository}";
                    
                    // Cache the result for 15 minutes
                    AddToCache(cacheKey, latestRelease, TimeSpan.FromMinutes(15));
                }

                return latestRelease;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error getting releases for {repo.Owner}/{repo.Repository}: {ex.Message}");
            return null;
        }
    }

    public async Task<GitHubCommit> GetCommitDetailsAsync(GitHubRelease release, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(release.TargetCommitish))
            throw new InvalidOperationException("No commit hash available for this release.");

        if (string.IsNullOrEmpty(release.BaseRepoUrl))
            throw new InvalidOperationException("Unable to determine repository information from release URL.");

        var cacheKey = $"commit_{release.BaseRepoUrl}_{release.TargetCommitish}";
        
        if (TryGetFromCache<GitHubCommit>(cacheKey, out var cachedCommit))
        {
            return cachedCommit;
        }

        try
        {
            var url = $"{release.BaseRepoUrl}/commits/{release.TargetCommitish}";
            using var response = await ExecuteWithRetryAsync(() => CreateRequest(url), cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            {
                var commit = _jsonSerializer.DeserializeFromStream<GitHubCommit>(stream);
                AddToCache(cacheKey, commit, TimeSpan.FromHours(1));
                return commit;
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Error fetching commit details: {ex.Message}", ex);
        }
    }

    public async Task<string> DownloadReleaseAsync(GitHubRelease release, string destinationPath, IProgress<double> progress = null, CancellationToken cancellationToken = default)
    {
        var (tempPath, fileName) = await DownloadReleaseToTempAsync(release, destinationPath, cancellationToken).ConfigureAwait(false);
        var fullPath = Path.Combine(destinationPath, fileName);

        try
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
            File.Move(tempPath, fullPath);
        }
        catch (Exception ex)
        {
            if (File.Exists(tempPath))
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }

            throw new Exception($"Error downloading release: {ex.Message}", ex);
        }

        return fileName;
    }

    /// <summary>Downloads the release DLL next to its final location as <c>name.dll.temp</c>, which Emby never loads.</summary>
    /// <returns>The temp file path and the DLL file name it should be installed as.</returns>
    public async Task<(string TempPath, string FileName)> DownloadReleaseToTempAsync(GitHubRelease release, string destinationPath, CancellationToken cancellationToken = default)
    {
        var dllAsset = release.Assets?.FirstOrDefault(x => x.IsDll);
        if (dllAsset == null)
        {
            _logger.Error($"No DLL asset found for release {release.Url}");
            throw new InvalidOperationException("No DLL asset found for this release.");
        }

        // For private repos, use the API URL instead of browser download URL
        var downloadUrl = !string.IsNullOrEmpty(_githubToken) 
            ? dllAsset.Url  // Use API URL for authenticated requests
            : dllAsset.BrowserDownloadUrl; // Use browser URL for public repos
            
        if (string.IsNullOrEmpty(downloadUrl))
        {
            _logger.Error($"No download URL available for release {release.Url} that has a DLL asset.");
            throw new InvalidOperationException("No download URL available for this release that has a DLL asset.");
        }
        
        _logger.Debug($"Using download URL: {downloadUrl}");

        var fileName = downloadUrl == dllAsset.Url ? dllAsset.Name : Path.GetFileName(downloadUrl);
        var tempPath = Path.Combine(destinationPath, $"{fileName}.temp");

        try
        {
            // For API URLs, we need to accept octet-stream
            var accept = downloadUrl == dllAsset.Url ? "application/octet-stream" : "application/json";

            using var response = await ExecuteWithRetryAsync(() => CreateRequest(downloadUrl, accept), cancellationToken,
                                                             HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            // First download to a temporary file
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Clean up temp file if it exists
            if (File.Exists(tempPath))
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }

            throw new Exception($"Error downloading release: {ex.Message}", ex);
        }

        return (tempPath, fileName);
    }

    public async Task<bool> ValidateRepositoryAsync(string owner, string repository, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{owner}/{repository}";
            using var response = await ExecuteWithRetryAsync(() => CreateRequest(url), cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // Basic retry logic without external dependencies. A fresh request is built per attempt
    // because HttpRequestMessage cannot be sent twice.
    private async Task<HttpResponseMessage> ExecuteWithRetryAsync(Func<HttpRequestMessage> requestFactory,
                                                                  CancellationToken cancellationToken,
                                                                  HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
                                                                  int maxRetries = 2)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var request  = requestFactory();
                var       response = await _client.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
                
                // Check if we should retry based on status code
                if (ShouldRetry(response.StatusCode) && attempt < maxRetries)
                {
                    response.Dispose();
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
                    _logger.Warn($"Request failed with {response.StatusCode}, retrying in {delay.TotalSeconds} seconds (attempt {attempt + 1}/{maxRetries + 1})");
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                
                return response;
            }
            catch (HttpRequestException ex) when (attempt < maxRetries && !cancellationToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
                _logger.Warn($"Request failed with {ex.GetType().Name}: {ex.Message}, retrying in {delay.TotalSeconds} seconds (attempt {attempt + 1}/{maxRetries + 1})");
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.RequestTimeout ||
               statusCode == HttpStatusCode.InternalServerError ||
               statusCode == HttpStatusCode.BadGateway ||
               statusCode == HttpStatusCode.ServiceUnavailable ||
               statusCode == HttpStatusCode.GatewayTimeout ||
               statusCode == HttpStatusCode.TooManyRequests;
    }

    // Simple cache methods
    private bool TryGetFromCache<T>(string key, out T value)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(key, out var cacheItem) && !cacheItem.IsExpired)
            {
                value = (T)cacheItem.Data;
                return true;
            }
            
            // Remove expired item
            if (_cache.ContainsKey(key))
            {
                _cache.Remove(key);
            }
            
            value = default(T);
            return false;
        }
    }

    private void AddToCache<T>(string key, T value, TimeSpan expiry)
    {
        lock (_cacheLock)
        {
            _cache[key] = new CacheItem
            {
                Data = value,
                ExpiryTime = DateTime.UtcNow.Add(expiry)
            };
            
            // Simple cleanup - remove expired items if cache gets too large
            if (_cache.Count > 100)
            {
                var expiredKeys = _cache.Where(kvp => kvp.Value.IsExpired).Select(kvp => kvp.Key).ToList();
                foreach (var expiredKey in expiredKeys)
                {
                    _cache.Remove(expiredKey);
                }
            }
        }
    }

    public async Task<List<PluginRegistryEntry>> GetAllRegistryPluginsAsync(List<PluginRegistry> registries, CancellationToken cancellationToken = default)
    {
        var enabled = registries.Where(r => r.Enabled).ToList();

        // Fetch all registries concurrently, then merge in configured order so dedupe stays deterministic
        var registryData = await Task.WhenAll(enabled.Select(r => GetRegistryDataAsync(r, cancellationToken))).ConfigureAwait(false);

        var allPlugins = new List<PluginRegistryEntry>();
        for (var i = 0; i < enabled.Count; i++)
        {
            var registry = enabled[i];
            if (registryData[i]?.Plugins == null) continue;

            foreach (var plugin in registryData[i].Plugins)
            {
                // Check for duplicates by URL (case-insensitive)
                var existingPlugin = allPlugins.FirstOrDefault(p => 
                    string.Equals(p.Url, plugin.Url, StringComparison.OrdinalIgnoreCase));
                
                if (existingPlugin == null)
                {
                    // Copy so cached registry data is never mutated
                    allPlugins.Add(new PluginRegistryEntry
                                   {
                                       Name           = plugin.Name,
                                       Description    = plugin.Description,
                                       Url            = plugin.Url,
                                       RegistrySource = registry.Name
                                   });
                }
                else
                {
                    _logger.Debug($"Skipping duplicate plugin URL: {plugin.Url} from {registry.Name} (already exists from {existingPlugin.RegistrySource})");
                }
            }
        }
        
        return allPlugins;
    }

    private async Task<PluginRegistryData> GetRegistryDataAsync(PluginRegistry registry, CancellationToken cancellationToken)
    {
        try
        {
            // Handle embedded registry
            if (registry.RawUrl == "embedded://default")
            {
                var assembly = typeof(GitHubApiClient).Assembly;
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("DefaultRegistry.plugins.json"));

                if (resourceName == null) return null;

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    return _jsonSerializer.DeserializeFromStream<PluginRegistryData>(stream);
                }
            }

            var cacheKey = $"registry_{registry.RawUrl}";
            if (TryGetFromCache<PluginRegistryData>(cacheKey, out var cached))
                return cached;

            // Handle regular URL registries
            using var response = await ExecuteWithRetryAsync(() => CreateRequest(registry.RawUrl), cancellationToken, maxRetries: 0).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            {
                var data = _jsonSerializer.DeserializeFromStream<PluginRegistryData>(stream);
                AddToCache(cacheKey, data, TimeSpan.FromMinutes(15));
                return data;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to fetch registry {registry.Name} from {registry.RawUrl}: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        // The HttpClient is either shared or owned by the caller
    }
}