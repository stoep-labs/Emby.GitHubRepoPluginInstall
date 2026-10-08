using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Emby.GitHubRepoPluginInstall.GithubAPI;
using Emby.GitHubRepoPluginInstall.Models;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using NSubstitute;
using Xunit;

namespace Emby.GitHubRepoPluginInstall.Tests;

public class GitHubApiClientTests
{
    private class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses;

        public RecordingHandler(params HttpStatusCode[] statuses) => _statuses = new Queue<HttpStatusCode>(statuses);

        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var status = _statuses.Count > 0 ? _statuses.Dequeue() : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) });
        }
    }

    private static IJsonSerializer SerializerReturning(List<GitHubRelease> releases)
    {
        var serializer = Substitute.For<IJsonSerializer>();
        serializer.DeserializeFromStream<List<GitHubRelease>>(Arg.Any<Stream>()).Returns(releases);
        return serializer;
    }

    [Fact]
    public async Task GetLatestRelease_IssuesSingleAuthenticatedRequest()
    {
        var handler = new RecordingHandler();
        var release = new GitHubRelease { TagName = "v2", TargetCommitish = "main", PublishedAt = DateTimeOffset.UtcNow };
        var client  = new GitHubApiClient("token", new HttpClient(handler), SerializerReturning(new List<GitHubRelease> { release }),
                                          Substitute.For<ILogger>());
        var repo = new ReposToProcess { Url = "https://github.com/owner/single-request" };

        var result = await client.GetLatestReleaseAsync(repo, true);

        Assert.Equal("v2", result.TagName);
        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/repos/owner/single-request/releases", request.RequestUri.AbsolutePath);
        Assert.Equal("Bearer token", request.Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task GetLatestRelease_SkipsDraftReleases()
    {
        var handler = new RecordingHandler();
        var releases = new List<GitHubRelease>
                       {
                           new GitHubRelease { TagName = "draft", Draft = true, PublishedAt = DateTimeOffset.UtcNow },
                           new GitHubRelease { TagName = "v1", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1) }
                       };
        var client = new GitHubApiClient("token", new HttpClient(handler), SerializerReturning(releases), Substitute.For<ILogger>());

        var result = await client.GetLatestReleaseAsync(new ReposToProcess { Url = "https://github.com/owner/drafts" }, true);

        Assert.Equal("v1", result.TagName);
    }

    [Fact]
    public async Task DownloadRelease_RetriesWithFreshRequestAfterServerError()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var client  = new GitHubApiClient(null, new HttpClient(handler), Substitute.For<IJsonSerializer>(), Substitute.For<ILogger>());
        var release = new GitHubRelease
                      {
                          Assets = new List<GitHubReleaseAsset>
                                   {
                                       new GitHubReleaseAsset
                                       {
                                           Name               = "Plugin.dll",
                                           Url                = "https://api.github.com/assets/1",
                                           BrowserDownloadUrl = "https://github.com/owner/repo/releases/download/v1/Plugin.dll"
                                       }
                                   }
                      };
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var fileName = await client.DownloadReleaseAsync(release, dir);

            Assert.Equal("Plugin.dll", fileName);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(dir, fileName)));
            Assert.Equal(2, handler.Requests.Count);
            Assert.NotSame(handler.Requests[0], handler.Requests[1]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
