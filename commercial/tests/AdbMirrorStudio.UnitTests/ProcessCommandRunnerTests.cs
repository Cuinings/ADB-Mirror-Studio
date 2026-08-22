using AdbMirrorStudio.Application.Commands;
using AdbMirrorStudio.Infrastructure.Processes;

namespace AdbMirrorStudio.UnitTests;

public sealed class ProcessCommandRunnerTests
{
    [Fact]
    public async Task RunAsync_TerminatesProcessTreeAfterTimeout()
    {
        var runner = new ProcessCommandRunner();
        var request = LongRunningCommand(TimeSpan.FromMilliseconds(150));

        var result = await runner.RunAsync(request);

        Assert.True(result.TimedOut);
        Assert.False(result.Cancelled);
    }

    [Fact]
    public async Task RunAsync_TerminatesProcessTreeAfterCancellation()
    {
        var runner = new ProcessCommandRunner();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var result = await runner.RunAsync(LongRunningCommand(TimeSpan.FromSeconds(10)), cancellation.Token);

        Assert.False(result.TimedOut);
        Assert.True(result.Cancelled);
    }

    private static CommandRequest LongRunningCommand(TimeSpan timeout)
    {
        var command = Environment.GetEnvironmentVariable("ComSpec")
            ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");
        return new CommandRequest(
            command,
            ["/d", "/c", "ping 127.0.0.1 -n 6 > nul"],
            Timeout: timeout);
    }
}
