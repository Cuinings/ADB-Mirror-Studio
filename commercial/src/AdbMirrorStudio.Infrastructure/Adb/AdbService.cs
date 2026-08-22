using AdbMirrorStudio.Application.Adb;
using AdbMirrorStudio.Application.Commands;
using AdbMirrorStudio.Domain.Devices;

namespace AdbMirrorStudio.Infrastructure.Adb;

public sealed class AdbService(ICommandRunner commandRunner, string adbPath) : IAdbService
{
    public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(["devices", "-l"], TimeSpan.FromSeconds(15), cancellationToken);
        return AdbOutputParser.ParseDevices(result.StandardOutput);
    }

    public async Task<IReadOnlyList<MdnsService>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(["mdns", "services"], TimeSpan.FromSeconds(10), cancellationToken);
        return AdbOutputParser.ParseMdnsServices(result.StandardOutput);
    }

    public async Task<string> ConnectAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var normalized = AdbEndpoint.Normalize(endpoint);
        var result = await ExecuteAsync(["connect", normalized], TimeSpan.FromSeconds(30), cancellationToken);
        return FirstOutput(result);
    }

    public async Task<string> PairAsync(string endpoint, string pairingCode, CancellationToken cancellationToken = default)
    {
        var normalized = AdbEndpoint.Normalize(endpoint);
        if (string.IsNullOrWhiteSpace(pairingCode)) throw new ArgumentException("配对码不能为空。", nameof(pairingCode));

        var request = new CommandRequest(
            adbPath,
            ["pair", normalized, pairingCode.Trim()],
            Path.GetDirectoryName(adbPath),
            Timeout: TimeSpan.FromSeconds(30),
            SensitiveArguments: true);
        var result = await commandRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result);
        return FirstOutput(result);
    }

    public async Task DisconnectAsync(string serial, CancellationToken cancellationToken = default) =>
        _ = await ExecuteAsync(["disconnect", serial], TimeSpan.FromSeconds(15), cancellationToken);

    public async Task RebootAsync(string serial, CancellationToken cancellationToken = default) =>
        _ = await ExecuteAsync(["-s", serial, "reboot"], TimeSpan.FromSeconds(30), cancellationToken);

    public async Task<string> EnableTcpIpAsync(string serial, int port = 5555, CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port), "端口必须在 1 到 65535 之间。");
        var result = await ExecuteAsync(["-s", serial.Trim(), "tcpip", port.ToString()], TimeSpan.FromSeconds(30), cancellationToken);
        return FirstOutput(result);
    }

    public async Task<string> InstallApkAsync(
        string serial,
        string apkPath,
        CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        ValidateLocalFile(apkPath, ".apk");
        var result = await ExecuteAsync(
            ["-s", serial.Trim(), "install", "-r", Path.GetFullPath(apkPath)],
            TimeSpan.FromMinutes(3),
            cancellationToken);
        return FirstOutput(result);
    }

    public async Task<string> PushFileAsync(
        string serial,
        string localPath,
        string remoteDirectory = "/sdcard/Download/",
        CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        ValidateLocalFile(localPath);
        if (string.IsNullOrWhiteSpace(remoteDirectory))
        {
            throw new ArgumentException("远程目录不能为空。", nameof(remoteDirectory));
        }

        var normalizedDirectory = remoteDirectory.Trim().Replace('\\', '/').TrimEnd('/');
        var remotePath = $"{normalizedDirectory}/{Path.GetFileName(localPath)}";
        var result = await ExecuteAsync(
            ["-s", serial.Trim(), "push", Path.GetFullPath(localPath), remotePath],
            TimeSpan.FromMinutes(5),
            cancellationToken);
        return FirstOutput(result);
    }

    public async Task<bool> IsOnlineAsync(string serial, CancellationToken cancellationToken = default)
    {
        var result = await commandRunner.RunAsync(
            new CommandRequest(adbPath, ["-s", serial, "get-state"], Path.GetDirectoryName(adbPath), Timeout: TimeSpan.FromSeconds(10)),
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess && result.StandardOutput.Trim() == "device";
    }

    private async Task<CommandResult> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(adbPath)) throw new FileNotFoundException("未找到 adb.exe。", adbPath);
        var result = await commandRunner.RunAsync(
            new CommandRequest(adbPath, arguments, Path.GetDirectoryName(adbPath), Timeout: timeout),
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result);
        return result;
    }

    private static void EnsureSuccess(CommandResult result)
    {
        if (result.Cancelled) throw new OperationCanceledException("ADB 操作已取消。");
        if (result.TimedOut) throw new AdbCommandException("ADB 操作超时。");
        if (result.ExitCode != 0)
        {
            throw new AdbCommandException(FirstOutput(result), result.ExitCode);
        }
    }

    private static void ValidateSerial(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new ArgumentException("请选择目标设备。", nameof(serial));
        }
    }

    private static void ValidateLocalFile(string path, string? requiredExtension = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("请选择本地文件。", nameof(path));
        }
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("所选文件不存在。", path);
        }
        if (requiredExtension is not null && !string.Equals(Path.GetExtension(path), requiredExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"请选择 {requiredExtension} 文件。", nameof(path));
        }
    }

    private static string FirstOutput(CommandResult result) =>
        (string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardError : result.StandardOutput).Trim();
}
