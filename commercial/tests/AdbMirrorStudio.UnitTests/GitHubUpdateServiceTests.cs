using System.Net;
using System.Text;
using AdbMirrorStudio.Infrastructure.Updates;

namespace AdbMirrorStudio.UnitTests;

public sealed class GitHubUpdateServiceTests
{
    [Fact]
    public async Task CheckAsync_ReturnsDownloadWhenNewerVersionExists()
    {
        using var client = ClientFor("V1.2.0");
        var service = new GitHubUpdateService(client, "V1.0.0", "owner", "repo");

        var update = await service.CheckAsync();

        Assert.True(update.IsUpdateAvailable);
        Assert.Equal("V1.2.0", update.LatestVersion);
        Assert.Equal("https://example.test/app-win-x64.zip", update.DownloadUrl);
    }

    [Fact]
    public async Task CheckAsync_DoesNotOfferSameVersion()
    {
        using var client = ClientFor("v1.0.0");
        var service = new GitHubUpdateService(client, "V1.0.0", "owner", "repo");

        var update = await service.CheckAsync();

        Assert.False(update.IsUpdateAvailable);
        Assert.Equal("V1.0.0", update.CurrentVersion);
    }

    private static HttpClient ClientFor(string tag) => new(new StubHandler($$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://example.test/release",
          "body": "notes",
          "assets": [
            { "name": "app-win-x64.zip", "browser_download_url": "https://example.test/app-win-x64.zip" }
          ]
        }
        """));

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }
}
