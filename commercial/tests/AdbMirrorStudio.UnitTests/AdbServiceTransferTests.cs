using AdbMirrorStudio.Application.Commands;
using AdbMirrorStudio.Application.Adb;
using AdbMirrorStudio.Infrastructure.Adb;

namespace AdbMirrorStudio.UnitTests;

public sealed class AdbServiceTransferTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"adb-mirror-tests-{Guid.NewGuid():N}");

    public AdbServiceTransferTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task InstallApkAsync_PassesPathAsSingleArgument()
    {
        var adbPath = CreateFile("adb.exe");
        var apkPath = CreateFile("my app.apk");
        var runner = new CapturingRunner("Success");
        var service = new AdbService(runner, adbPath);

        var output = await service.InstallApkAsync("device-1", apkPath);

        Assert.Equal("Success", output);
        Assert.Equal(["-s", "device-1", "install", "-r", Path.GetFullPath(apkPath)], runner.LastRequest!.Arguments);
        Assert.Equal(TimeSpan.FromMinutes(3), runner.LastRequest.Timeout);
    }

    [Fact]
    public async Task PushFileAsync_BuildsDownloadDestination()
    {
        var adbPath = CreateFile("adb.exe");
        var localPath = CreateFile("季度 报告.pdf");
        var runner = new CapturingRunner("1 file pushed");
        var service = new AdbService(runner, adbPath);

        await service.PushFileAsync("device-2", localPath);

        Assert.Equal(["-s", "device-2", "push", Path.GetFullPath(localPath), "/sdcard/Download/季度 报告.pdf"], runner.LastRequest!.Arguments);
        Assert.Equal(TimeSpan.FromMinutes(5), runner.LastRequest.Timeout);
    }

    [Fact]
    public async Task InstallApkAsync_RejectsMissingFileBeforeStartingProcess()
    {
        var adbPath = CreateFile("adb.exe");
        var runner = new CapturingRunner("unused");
        var service = new AdbService(runner, adbPath);

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.InstallApkAsync("device-1", Path.Combine(_directory, "missing.apk")));

        Assert.Null(runner.LastRequest);
    }

    [Fact]
    public async Task EnableTcpIpAsync_ValidatesAndPassesPort()
    {
        var adbPath = CreateFile("adb.exe");
        var runner = new CapturingRunner("restarting in TCP mode port: 4321");
        var service = new AdbService(runner, adbPath);

        await service.EnableTcpIpAsync("usb-device", 4321);

        Assert.Equal(["-s", "usb-device", "tcpip", "4321"], runner.LastRequest!.Arguments);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.EnableTcpIpAsync("usb-device", 70000));
    }

    [Fact]
    public async Task PullFileAsync_UsesRemoteAbsolutePathAndLocalDirectory()
    {
        var adbPath = CreateFile("adb.exe");
        var destination = Path.Combine(_directory, "downloads");
        var runner = new CapturingRunner("1 file pulled");
        var service = new AdbService(runner, adbPath);

        await service.PullFileAsync("device", "/sdcard/Download/report.pdf", destination);

        Assert.Equal(["-s", "device", "pull", "/sdcard/Download/report.pdf", Path.GetFullPath(destination)], runner.LastRequest!.Arguments);
        await Assert.ThrowsAsync<ArgumentException>(() => service.PullFileAsync("device", "relative.txt", destination));
    }

    [Fact]
    public async Task CaptureScreenshotAsync_CapturesPullsAndRemovesTemporaryFile()
    {
        var adbPath = CreateFile("adb.exe");
        var runner = new CapturingRunner("ok");
        var service = new AdbService(runner, adbPath);
        var destination = Path.Combine(_directory, "screen shot.png");

        var result = await service.CaptureScreenshotAsync("device", destination);

        Assert.Equal(Path.GetFullPath(destination), result);
        Assert.Equal(3, runner.Requests.Count);
        Assert.Equal("screencap", runner.Requests[0].Arguments[3]);
        Assert.Equal("pull", runner.Requests[1].Arguments[2]);
        Assert.Equal("rm", runner.Requests[2].Arguments[3]);
    }

    [Fact]
    public async Task CaptureScreenshotAsync_RemovesTemporaryFileWhenCaptureFails()
    {
        var adbPath = CreateFile("adb.exe");
        var runner = new SequenceRunner(
            new CommandResult(1, string.Empty, "capture failed", TimeSpan.Zero, false, false),
            new CommandResult(0, string.Empty, string.Empty, TimeSpan.Zero, false, false));
        var service = new AdbService(runner, adbPath);

        await Assert.ThrowsAsync<AdbCommandException>(() =>
            service.CaptureScreenshotAsync("device", Path.Combine(_directory, "failed.png")));

        Assert.Equal(2, runner.Requests.Count);
        Assert.Equal("screencap", runner.Requests[0].Arguments[3]);
        Assert.Equal("rm", runner.Requests[1].Arguments[3]);
    }

    [Fact]
    public async Task GetDeviceDetailsAsync_PrefersOverrideResolution()
    {
        var adbPath = CreateFile("adb.exe");
        var runner = new SequenceRunner(
            Success("15"),
            Success("35"),
            Success("Physical size: 1440x3200\nOverride size: 1080x2400"),
            Success("level: 88\nstatus: 2"),
            Success("Filesystem Size Used Avail Use% Mounted on\n/data 100G 40G 60G 40% /data"));
        var service = new AdbService(runner, adbPath);

        var details = await service.GetDeviceDetailsAsync("device");

        Assert.Equal("1080x2400", details.Resolution);
        Assert.Equal(88, details.BatteryLevel);
    }

    [Fact]
    public async Task GetDeviceDetailsAsync_CollectsIndependentFieldsConcurrently()
    {
        var adbPath = CreateFile("adb.exe");
        var runner = new ConcurrentDetailsRunner();
        var service = new AdbService(runner, adbPath);

        await service.GetDeviceDetailsAsync("device");

        Assert.True(runner.MaxConcurrency > 1);
        Assert.Equal(5, runner.RequestCount);
    }

    [Fact]
    public async Task GetLogcatSnapshotAsync_UsesBoundedLineCount()
    {
        var adbPath = CreateFile("adb.exe");
        var runner = new CapturingRunner("log line");
        var service = new AdbService(runner, adbPath);

        var output = await service.GetLogcatSnapshotAsync("device", 750);

        Assert.Equal("log line", output);
        Assert.Equal(["-s", "device", "logcat", "-d", "-t", "750"], runner.LastRequest!.Arguments);
    }

    [Fact]
    public async Task SendKeyEventAsync_UsesValidatedKeyCode()
    {
        var runner = new CapturingRunner(string.Empty);
        var service = new AdbService(runner, CreateFile("adb.exe"));

        await service.SendKeyEventAsync("device", 187);

        Assert.Equal(["-s", "device", "shell", "input", "keyevent", "187"], runner.LastRequest!.Arguments);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SendKeyEventAsync("device", 1000));
    }

    [Fact]
    public async Task GetInstalledAppsAsync_ParsesAndSortsUserPackages()
    {
        var runner = new CapturingRunner("package:com.zeta.app\npackage:com.alpha.app\n");
        var service = new AdbService(runner, CreateFile("adb.exe"));

        var apps = await service.GetInstalledAppsAsync("device");

        Assert.Equal(["com.alpha.app", "com.zeta.app"], apps.Select(app => app.PackageName));
        Assert.Equal(["-s", "device", "shell", "pm", "list", "packages", "-3"], runner.LastRequest!.Arguments);
    }

    [Fact]
    public async Task AppActions_PassPackageAsSingleValidatedArgument()
    {
        var runner = new CapturingRunner("Success");
        var service = new AdbService(runner, CreateFile("adb.exe"));

        await service.LaunchAppAsync("device", "com.example.app");
        Assert.Contains("com.example.app", runner.LastRequest!.Arguments);
        await service.ForceStopAppAsync("device", "com.example.app");
        Assert.Equal(["-s", "device", "shell", "am", "force-stop", "com.example.app"], runner.LastRequest!.Arguments);
        await service.UninstallAppAsync("device", "com.example.app");
        Assert.Equal(["-s", "device", "uninstall", "com.example.app"], runner.LastRequest!.Arguments);
        await Assert.ThrowsAsync<ArgumentException>(() => service.LaunchAppAsync("device", "bad;package"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UninstallAppAsync("device", ".bad.package"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UninstallAppAsync("device", "bad..package"));
    }

    [Fact]
    public async Task RunShellCommandAsync_TargetsSelectedDeviceWithoutStartingWindowsShell()
    {
        var runner = new CapturingRunner("Pixel 9\n");
        var adbPath = CreateFile("adb.exe");
        var service = new AdbService(runner, adbPath);

        var output = await service.RunShellCommandAsync("device-2", "getprop ro.product.model");

        Assert.Equal("Pixel 9", output);
        Assert.Equal(adbPath, runner.LastRequest!.FileName);
        Assert.Equal(["-s", "device-2", "shell", "sh", "-c", "getprop ro.product.model"], runner.LastRequest.Arguments);
        Assert.True(runner.LastRequest.SensitiveArguments);
        Assert.DoesNotContain("cmd.exe", runner.LastRequest.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("getprop\nreboot")]
    public async Task RunShellCommandAsync_RejectsEmptyOrMultilineCommands(string command)
    {
        var runner = new CapturingRunner(string.Empty);
        var service = new AdbService(runner, CreateFile("adb.exe"));

        await Assert.ThrowsAsync<ArgumentException>(() => service.RunShellCommandAsync("device", command));
        Assert.Empty(runner.Requests);
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private static CommandResult Success(string output) =>
        new(0, output, string.Empty, TimeSpan.Zero, false, false);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class CapturingRunner(string output) : ICommandRunner
    {
        public CommandRequest? LastRequest { get; private set; }
        public List<CommandRequest> Requests { get; } = [];

        public Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            Requests.Add(request);
            return Task.FromResult(new CommandResult(0, output, string.Empty, TimeSpan.Zero, false, false));
        }
    }

    private sealed class SequenceRunner(params CommandResult[] results) : ICommandRunner
    {
        private readonly Queue<CommandResult> _results = new(results);
        public List<CommandRequest> Requests { get; } = [];

        public Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class ConcurrentDetailsRunner : ICommandRunner
    {
        private int _active;
        private int _maxConcurrency;
        private int _requestCount;
        public int MaxConcurrency => Volatile.Read(ref _maxConcurrency);
        public int RequestCount => Volatile.Read(ref _requestCount);

        public async Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _requestCount);
            var active = Interlocked.Increment(ref _active);
            while (true)
            {
                var maximum = Volatile.Read(ref _maxConcurrency);
                if (active <= maximum || Interlocked.CompareExchange(ref _maxConcurrency, active, maximum) == maximum) break;
            }

            try
            {
                await Task.Delay(50, cancellationToken);
                return Success(string.Empty);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }
}
