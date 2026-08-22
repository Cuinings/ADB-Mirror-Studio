using AdbMirrorStudio.Domain.Mirroring;

namespace AdbMirrorStudio.Application.Mirroring;

public interface IMirrorSessionManager : IAsyncDisposable
{
    IReadOnlyCollection<MirrorSession> ActiveSessions { get; }
    event EventHandler<MirrorSession>? SessionChanged;
    Task<MirrorSession> StartAsync(
        string deviceSerial,
        MirrorProfile profile,
        string? windowTitle = null,
        CancellationToken cancellationToken = default);
    Task StopAsync(string deviceSerial, CancellationToken cancellationToken = default);
}

