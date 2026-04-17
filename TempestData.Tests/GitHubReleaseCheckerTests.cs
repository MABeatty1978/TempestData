using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TempestData.Services;
using Xunit;

namespace TempestData.Tests;

public class GitHubReleaseCheckerTests
{
    [Fact]
    public async Task CheckForUpdatesAsync_WhenLatestIsNewer_ReturnsUpdateAvailable()
    {
        var payload = "{\"tag_name\":\"v1.2.0\",\"html_url\":\"https://github.com/MABeatty1978/TempestData/releases/tag/v1.2.0\",\"prerelease\":false}";
        var checker = CreateChecker(payload, HttpStatusCode.OK);

        var result = await checker.CheckForUpdatesAsync("1.0.0");

        Assert.True(result.IsSuccess);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.0.0", result.CurrentVersion);
        Assert.Equal("1.2.0", result.LatestVersion);
        Assert.Equal("https://github.com/MABeatty1978/TempestData/releases/tag/v1.2.0", result.ReleaseUrl);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenVersionsMatch_ReturnsUpToDate()
    {
        var payload = "{\"tag_name\":\"v1.0.0\",\"html_url\":\"https://github.com/MABeatty1978/TempestData/releases/tag/v1.0.0\",\"prerelease\":false}";
        var checker = CreateChecker(payload, HttpStatusCode.OK);

        var result = await checker.CheckForUpdatesAsync("1.0.0");

        Assert.True(result.IsSuccess);
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("1.0.0", result.CurrentVersion);
        Assert.Equal("1.0.0", result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenReleaseIsPrerelease_IgnoresIt()
    {
        var payload = "{\"tag_name\":\"v1.1.0-beta.1\",\"html_url\":\"https://github.com/MABeatty1978/TempestData/releases/tag/v1.1.0-beta.1\",\"prerelease\":true}";
        var checker = CreateChecker(payload, HttpStatusCode.OK);

        var result = await checker.CheckForUpdatesAsync("1.0.0");

        Assert.True(result.IsSuccess);
        Assert.False(result.IsUpdateAvailable);
        Assert.True(result.IsPrerelease);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenTagIsMalformed_ReturnsFailure()
    {
        var payload = "{\"tag_name\":\"release-abc\",\"html_url\":\"https://github.com/MABeatty1978/TempestData/releases/tag/release-abc\",\"prerelease\":false}";
        var checker = CreateChecker(payload, HttpStatusCode.OK);

        var result = await checker.CheckForUpdatesAsync("1.0.0");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenHttpFails_ReturnsFailure()
    {
        var checker = CreateChecker("{}", HttpStatusCode.Forbidden);

        var result = await checker.CheckForUpdatesAsync("1.0.0");

        Assert.False(result.IsSuccess);
        Assert.Contains("GitHub release check failed", result.ErrorMessage ?? string.Empty);
    }

    private static GitHubReleaseChecker CreateChecker(string payload, HttpStatusCode statusCode)
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });

        return new GitHubReleaseChecker(new HttpClient(handler));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
