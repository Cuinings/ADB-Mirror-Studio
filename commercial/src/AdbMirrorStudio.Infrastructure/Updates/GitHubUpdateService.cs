using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AdbMirrorStudio.Application.Updates;
using AdbMirrorStudio.Infrastructure.Serialization;

namespace AdbMirrorStudio.Infrastructure.Updates;

public sealed class GitHubUpdateService(
    HttpClient httpClient,
    string currentVersion,
    string owner,
    string repository) : IUpdateService
{
    public async Task<AppUpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{owner}/{repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("ADB-Mirror-Studio-UpdateChecker/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync(
                AdbMirrorStudioJsonContext.Default.GitHubRelease,
                cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("GitHub 返回了空的版本信息。");

        var current = ParseVersion(currentVersion);
        var latest = ParseVersion(release.TagName);
        var asset = release.Assets.FirstOrDefault(item =>
            item.Name.EndsWith("win-x64.zip", StringComparison.OrdinalIgnoreCase));

        return new AppUpdateInfo(
            NormalizeDisplayVersion(currentVersion),
            NormalizeDisplayVersion(release.TagName),
            latest > current,
            release.HtmlUrl,
            asset?.BrowserDownloadUrl,
            release.Body);
    }

    private static Version ParseVersion(string value)
    {
        var normalized = value.Trim().TrimStart('v', 'V').Split(['+', '-'], 2)[0];
        return Version.TryParse(normalized, out var version)
            ? version
            : throw new FormatException($"无法识别版本号：{value}");
    }

    private static string NormalizeDisplayVersion(string value) =>
        $"V{ParseVersion(value).ToString(3)}";

}

internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset> Assets);

internal sealed record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
