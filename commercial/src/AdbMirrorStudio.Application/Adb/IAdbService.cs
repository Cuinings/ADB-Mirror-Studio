using AdbMirrorStudio.Domain.Devices;

namespace AdbMirrorStudio.Application.Adb;

public interface IAdbService
{
    Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MdnsService>> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<string> ConnectAsync(string endpoint, CancellationToken cancellationToken = default);
    Task<string> PairAsync(string endpoint, string pairingCode, CancellationToken cancellationToken = default);
    Task DisconnectAsync(string serial, CancellationToken cancellationToken = default);
    Task RebootAsync(string serial, CancellationToken cancellationToken = default);
    Task<string> EnableTcpIpAsync(string serial, int port = 5555, CancellationToken cancellationToken = default);
    Task<string> InstallApkAsync(string serial, string apkPath, CancellationToken cancellationToken = default);
    Task<string> PushFileAsync(string serial, string localPath, string remoteDirectory = "/sdcard/Download/", CancellationToken cancellationToken = default);
    Task<bool> IsOnlineAsync(string serial, CancellationToken cancellationToken = default);
}

public sealed class AdbCommandException(string message, int? exitCode = null) : Exception(message)
{
    public int? ExitCode { get; } = exitCode;
}
