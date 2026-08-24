using System.Net;
using System.Security.Cryptography;
using System.Text;
using AdbMirrorStudio.Application.Updates;
using AdbMirrorStudio.Infrastructure.Updates;

namespace AdbMirrorStudio.UnitTests;

public sealed class GitHubUpdateServiceTests
{
    [Fact]
    public async Task CheckAsync_ReturnsVerifiableInstallerWhenNewerVersionExists()
    {
        using var client = ClientFor("V1.2.0");
        var service = new GitHubUpdateService(client, "V1.0.0", "owner", "repo");

        var update = await service.CheckAsync();

        Assert.True(update.IsUpdateAvailable);
        Assert.Equal("V1.2.0", update.LatestVersion);
        Assert.NotNull(update.Installer);
        Assert.Equal("ADB-Mirror-Studio-Setup-V1.2.0-win-x64.exe", update.Installer.FileName);
        Assert.Equal(123456, update.Installer.Size);
        Assert.Equal(new string('A', 64), update.Installer.Sha256);
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

    [Fact]
    public async Task CheckAsync_NormalizesTwoComponentReleaseTag()
    {
        using var client = ClientFor("V1.0");
        var service = new GitHubUpdateService(client, "V1.0.0", "owner", "repo");

        var update = await service.CheckAsync();

        Assert.False(update.IsUpdateAvailable);
        Assert.Equal("V1.0.0", update.LatestVersion);
    }

    [Fact]
    public async Task CheckAsync_DoesNotOfferDirectInstallWithoutGitHubDigest()
    {
        using var client = ClientFor("V1.2.0", includeDigest: false);
        var service = new GitHubUpdateService(client, "V1.0.0", "owner", "repo");

        var update = await service.CheckAsync();

        Assert.True(update.IsUpdateAvailable);
        Assert.Null(update.Installer);
        Assert.Equal("https://example.test/release", update.ReleaseUrl);
    }

    [Fact]
    public async Task CheckAsync_RejectsInstallerWhoseNameDoesNotMatchReleaseVersion()
    {
        using var client = ClientFor("V1.2.0", installerTag: "V1.1.0");
        var service = new GitHubUpdateService(client, "V1.0.0", "owner", "repo");

        var update = await service.CheckAsync();

        Assert.True(update.IsUpdateAvailable);
        Assert.Null(update.Installer);
    }

    [Fact]
    public async Task DownloadInstallerAsync_VerifiesAndMovesCompletedPackage()
    {
        var payload = Encoding.UTF8.GetBytes("verified installer payload");
        var sha256 = Convert.ToHexString(SHA256.HashData(payload));
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        }));
        var service = new GitHubUpdateService(client, "V1.0.0", "owner", "repo");
        var update = UpdateFor(payload.Length, sha256);
        var directory = NewTemporaryDirectory();
        try
        {
            var progress = new List<UpdateDownloadProgress>();
            var result = await service.DownloadInstallerAsync(
                update,
                directory,
                new Progress<UpdateDownloadProgress>(item => progress.Add(item)));

            Assert.Equal(payload, await File.ReadAllBytesAsync(result));
            Assert.False(File.Exists(result + ".download"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadInstallerAsync_DeletesPackageWhenHashDoesNotMatch()
    {
        var payload = Encoding.UTF8.GetBytes("tampered installer payload");
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        }));
        var service = new GitHubUpdateService(client, "V1.0.0", "owner", "repo");
        var update = UpdateFor(payload.Length, new string('0', 64));
        var directory = NewTemporaryDirectory();
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.DownloadInstallerAsync(update, directory));

            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static HttpClient ClientFor(
        string tag,
        bool includeDigest = true,
        string? installerTag = null)
    {
        installerTag ??= tag;
        var digestJson = includeDigest ? $"\"sha256:{new string('A', 64)}\"" : "null";
        return new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://example.test/release",
          "body": "notes",
          "assets": [
            {
              "name": "ADB-Mirror-Studio-Setup-{{installerTag}}-win-x64.exe",
              "browser_download_url": "https://github.com/owner/repo/releases/download/{{tag}}/installer.exe",
              "size": 123456,
              "digest": {{digestJson}},
              "state": "uploaded"
            },
            {
              "name": "app-win-x64.zip",
              "browser_download_url": "https://github.com/owner/repo/releases/download/{{tag}}/app.zip",
              "size": 654321,
              "digest": "sha256:{{new string('B', 64)}}",
              "state": "uploaded"
            }
          ]
        }
        """, Encoding.UTF8, "application/json")
        }));
    }

    private static AppUpdateInfo UpdateFor(long size, string sha256) => new(
        "V1.0.0",
        "V1.2.0",
        true,
        "https://github.com/owner/repo/releases/tag/V1.2.0",
        new AppUpdatePackage(
            "ADB-Mirror-Studio-Setup-V1.2.0-win-x64.exe",
            "https://github.com/owner/repo/releases/download/V1.2.0/installer.exe",
            size,
            sha256),
        "notes");

    private static string NewTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AdbMirrorStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }
}
