namespace AdbMirrorStudio.Application.Updates;

public sealed record AppUpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    bool IsUpdateAvailable,
    string ReleaseUrl,
    string? DownloadUrl,
    string? ReleaseNotes);

public interface IUpdateService
{
    Task<AppUpdateInfo> CheckAsync(CancellationToken cancellationToken = default);
}
