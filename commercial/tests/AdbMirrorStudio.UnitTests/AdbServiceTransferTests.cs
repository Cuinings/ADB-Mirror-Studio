using AdbMirrorStudio.Application.Commands;
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

    private string CreateFile(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class CapturingRunner(string output) : ICommandRunner
    {
        public CommandRequest? LastRequest { get; private set; }

        public Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new CommandResult(0, output, string.Empty, TimeSpan.Zero, false, false));
        }
    }
}
