using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using AdbMirrorStudio.Application.Adb;
using AdbMirrorStudio.Application.Mirroring;
using AdbMirrorStudio.Domain.Mirroring;
using AdbMirrorStudio.Infrastructure.Processes;

namespace AdbMirrorStudio.Infrastructure.Scrcpy;

public sealed class MirrorSessionManager(IAdbService adbService, string scrcpyPath) : IMirrorSessionManager
{
    private readonly ConcurrentDictionary<string, ManagedSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new(StringComparer.Ordinal);
    private bool _disposed;

    public event EventHandler<MirrorSession>? SessionChanged;

    public IReadOnlyCollection<MirrorSession> ActiveSessions =>
        _sessions.Values.Select(value => value.Session).ToArray();

    public async Task<MirrorSession> StartAsync(
        string deviceSerial,
        MirrorProfile profile,
        string? windowTitle = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceSerial);
        if (!File.Exists(scrcpyPath)) throw new FileNotFoundException("未找到 scrcpy.exe。", scrcpyPath);

        var sessionLock = _sessionLocks.GetOrAdd(deviceSerial, _ => new SemaphoreSlim(1, 1));
        await sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sessions.TryGetValue(deviceSerial, out var existing))
            {
                if (IsRunning(existing.Process)) return existing.Session;
                _sessions.TryRemove(new KeyValuePair<string, ManagedSession>(deviceSerial, existing));
            }

            if (!await adbService.IsOnlineAsync(deviceSerial, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"设备 {deviceSerial} 当前不可达。");
            }

            var arguments = ScrcpyArgumentBuilder.Build(deviceSerial, profile, windowTitle);
            var startInfo = new ProcessStartInfo
            {
                FileName = scrcpyPath,
                WorkingDirectory = Path.GetDirectoryName(scrcpyPath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            startInfo.Environment["PATH"] = $"{startInfo.WorkingDirectory}{Path.PathSeparator}{Environment.GetEnvironmentVariable("PATH")}";

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var session = new MirrorSession(
                Guid.NewGuid().ToString("N"),
                deviceSerial,
                MirrorSessionState.Starting,
                null,
                DateTimeOffset.UtcNow);
            var managed = new ManagedSession(process, session);
            _sessions[deviceSerial] = managed;

            try
            {
                if (!process.Start()) throw new InvalidOperationException("scrcpy 进程未能启动。");
                managed.Session = session with { State = MirrorSessionState.Running, ProcessId = process.Id };
                RaiseChanged(managed.Session);
                _ = ObserveExitAsync(deviceSerial, managed);
                return managed.Session;
            }
            catch (Exception exception)
            {
                _sessions.TryRemove(new KeyValuePair<string, ManagedSession>(deviceSerial, managed));
                managed.Session = session with { State = MirrorSessionState.Failed, Error = exception.Message };
                RaiseChanged(managed.Session);
                process.Dispose();
                throw;
            }
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public async Task StopAsync(string deviceSerial, CancellationToken cancellationToken = default)
    {
        var sessionLock = _sessionLocks.GetOrAdd(deviceSerial, _ => new SemaphoreSlim(1, 1));
        await sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_sessions.TryGetValue(deviceSerial, out var managed)) return;
            managed.Session = managed.Session with { State = MirrorSessionState.Stopping };
            RaiseChanged(managed.Session);

            if (IsRunning(managed.Process))
            {
                managed.Process.Kill(entireProcessTree: true);
                await managed.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            sessionLock.Release();
        }
    }

    private async Task ObserveExitAsync(string serial, ManagedSession managed)
    {
        try
        {
            var stdoutTask = BoundedTextTailReader.ReadAsync(managed.Process.StandardOutput);
            var stderrTask = BoundedTextTailReader.ReadAsync(managed.Process.StandardError);
            await managed.Process.WaitForExitAsync().ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var exitCode = managed.Process.ExitCode;
            var error = exitCode == 0 ? null : LastMeaningfulLine(stderr, stdout);
            managed.Session = managed.Session with
            {
                State = exitCode == 0 ? MirrorSessionState.Exited : MirrorSessionState.Failed,
                ExitCode = exitCode,
                Error = error
            };
            RaiseChanged(managed.Session);
        }
        catch (Exception exception)
        {
            managed.Session = managed.Session with { State = MirrorSessionState.Failed, Error = exception.Message };
            RaiseChanged(managed.Session);
        }
        finally
        {
            var sessionLock = _sessionLocks.GetOrAdd(serial, _ => new SemaphoreSlim(1, 1));
            await sessionLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _sessions.TryRemove(new KeyValuePair<string, ManagedSession>(serial, managed));
                managed.Process.Dispose();
            }
            finally
            {
                sessionLock.Release();
            }
        }
    }

    private static bool IsRunning(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }
    }

    private static string? LastMeaningfulLine(params string[] outputs) =>
        outputs.SelectMany(output => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Select(line => line.Trim())
            .LastOrDefault(line => line.Length > 0);

    private void RaiseChanged(MirrorSession session) => SessionChanged?.Invoke(this, session);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var serial in _sessions.Keys.ToArray())
        {
            await StopAsync(serial).ConfigureAwait(false);
        }
    }

    private sealed class ManagedSession(Process process, MirrorSession session)
    {
        public Process Process { get; } = process;
        public MirrorSession Session { get; set; } = session;
    }
}
