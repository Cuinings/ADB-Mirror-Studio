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

    public async Task DisconnectAsync(string serial, CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        _ = await ExecuteAsync(["disconnect", serial.Trim()], TimeSpan.FromSeconds(15), cancellationToken);
    }

    public async Task RebootAsync(string serial, CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        _ = await ExecuteAsync(["-s", serial.Trim(), "reboot"], TimeSpan.FromSeconds(30), cancellationToken);
    }

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

    public async Task<DeviceDetails> GetDeviceDetailsAsync(string serial, CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        var normalizedSerial = serial.Trim();
        var androidTask = ExecuteAsync(
            ["-s", normalizedSerial, "shell", "getprop", "ro.build.version.release"],
            TimeSpan.FromSeconds(10), cancellationToken);
        var apiTask = ExecuteAsync(
            ["-s", normalizedSerial, "shell", "getprop", "ro.build.version.sdk"],
            TimeSpan.FromSeconds(10), cancellationToken);
        var sizeTask = ExecuteAsync(
            ["-s", normalizedSerial, "shell", "wm", "size"],
            TimeSpan.FromSeconds(10), cancellationToken);
        var batteryTask = ExecuteAsync(
            ["-s", normalizedSerial, "shell", "dumpsys", "battery"],
            TimeSpan.FromSeconds(10), cancellationToken);
        var storageTask = ExecuteAsync(
            ["-s", normalizedSerial, "shell", "df", "-h", "/data"],
            TimeSpan.FromSeconds(15), cancellationToken);

        await Task.WhenAll(androidTask, apiTask, sizeTask, batteryTask, storageTask).ConfigureAwait(false);
        var android = FirstOutput(await androidTask.ConfigureAwait(false));
        var api = FirstOutput(await apiTask.ConfigureAwait(false));
        var sizeOutput = FirstOutput(await sizeTask.ConfigureAwait(false));
        var batteryOutput = FirstOutput(await batteryTask.ConfigureAwait(false));
        var storageOutput = FirstOutput(await storageTask.ConfigureAwait(false));

        return new DeviceDetails(
            normalizedSerial,
            EmptyFallback(android),
            EmptyFallback(api),
            ParseResolution(sizeOutput),
            ParseIntField(batteryOutput, "level"),
            ParseBatteryStatus(ParseIntField(batteryOutput, "status")),
            ParseStorage(storageOutput));
    }

    public async Task<string> CaptureScreenshotAsync(
        string serial,
        string localPath,
        CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        if (string.IsNullOrWhiteSpace(localPath)) throw new ArgumentException("请选择截图保存位置。", nameof(localPath));
        if (!string.Equals(Path.GetExtension(localPath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("截图必须保存为 PNG 文件。", nameof(localPath));
        }

        var fullPath = Path.GetFullPath(localPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var remotePath = $"/data/local/tmp/adb-mirror-{Guid.NewGuid():N}.png";
        try
        {
            await ExecuteAsync(["-s", serial.Trim(), "shell", "screencap", "-p", remotePath], TimeSpan.FromSeconds(30), cancellationToken);
            await ExecuteAsync(["-s", serial.Trim(), "pull", remotePath, fullPath], TimeSpan.FromMinutes(2), cancellationToken);
            return fullPath;
        }
        finally
        {
            try
            {
                await ExecuteAsync(["-s", serial.Trim(), "shell", "rm", "-f", remotePath], TimeSpan.FromSeconds(10), CancellationToken.None);
            }
            catch
            {
                // A temporary screenshot must not hide the primary result.
            }
        }
    }

    public async Task<string> GetLogcatSnapshotAsync(
        string serial,
        int maxLines = 500,
        CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        if (maxLines is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(maxLines));
        var result = await ExecuteAsync(
            ["-s", serial.Trim(), "logcat", "-d", "-t", maxLines.ToString()],
            TimeSpan.FromSeconds(30), cancellationToken);
        return result.StandardOutput;
    }

    public async Task<string> PullFileAsync(
        string serial,
        string remotePath,
        string localDirectory,
        CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        if (string.IsNullOrWhiteSpace(remotePath) || !remotePath.Trim().StartsWith('/'))
        {
            throw new ArgumentException("设备路径必须是以 / 开头的绝对路径。", nameof(remotePath));
        }
        if (string.IsNullOrWhiteSpace(localDirectory)) throw new ArgumentException("请选择本地保存目录。", nameof(localDirectory));
        var fullDirectory = Path.GetFullPath(localDirectory);
        Directory.CreateDirectory(fullDirectory);
        var result = await ExecuteAsync(
            ["-s", serial.Trim(), "pull", remotePath.Trim(), fullDirectory],
            TimeSpan.FromMinutes(5), cancellationToken);
        return FirstOutput(result);
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

    private static string EmptyFallback(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private static int? ParseIntField(string output, string name)
    {
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':', 2);
            if (parts.Length == 2 && parts[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(parts[1].Trim(), out var value)) return value;
        }
        return null;
    }

    private static string ParseResolution(string output)
    {
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var line = lines.FirstOrDefault(value => value.Contains("Override size:", StringComparison.OrdinalIgnoreCase))
            ?? lines.FirstOrDefault(value => value.Contains("Physical size:", StringComparison.OrdinalIgnoreCase))
            ?? lines.FirstOrDefault(value => value.Contains("size:", StringComparison.OrdinalIgnoreCase));
        return line is null ? "—" : line[(line.IndexOf(':') + 1)..].Trim();
    }

    private static string ParseBatteryStatus(int? status) => status switch
    {
        2 => "充电中",
        3 => "放电中",
        4 => "未充电",
        5 => "已充满",
        _ => "未知"
    };

    private static string ParseStorage(string output)
    {
        var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (line is null) return "—";
        var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return columns.Length >= 5 ? $"可用 {columns[3]} / 总计 {columns[1]}（已用 {columns[4]}）" : line.Trim();
    }
}
