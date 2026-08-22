using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AdbMirrorStudio.Application.Adb;
using AdbMirrorStudio.Application.Commands;
using AdbMirrorStudio.Application.Mirroring;
using AdbMirrorStudio.Domain.Mirroring;
using AdbMirrorStudio.Infrastructure.Processes;

namespace AdbMirrorStudio.Infrastructure.Scrcpy;

public sealed class MirrorSessionManager(IAdbService adbService, string scrcpyPath) : IMirrorSessionManager
{
    private readonly ConcurrentDictionary<string, ManagedSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _codecCache = new(StringComparer.Ordinal);
    private readonly ProcessCommandRunner _commandRunner = new();
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
                if (IsRunning(existing.Process))
                {
                    var sameConfiguration = string.Equals(existing.Session.RecordPath, profile.RecordPath, StringComparison.OrdinalIgnoreCase)
                        && existing.Session.MaxSize == profile.MaxSize
                        && existing.Session.MaxFps == profile.MaxFps
                        && existing.Session.VideoBitRateMbps == profile.VideoBitRateMbps;
                    if (sameConfiguration) return existing.Session;
                    throw new InvalidOperationException("该设备的镜像已经运行。更改录制或采集配置前，请先停止现有会话。");
                }
                _sessions.TryRemove(new KeyValuePair<string, ManagedSession>(deviceSerial, existing));
            }

            if (!await adbService.IsOnlineAsync(deviceSerial, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"设备 {deviceSerial} 当前不可达。");
            }

            var resolvedCodec = profile.VideoCodec.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? await ResolveCodecAsync(deviceSerial, profile, cancellationToken).ConfigureAwait(false)
                : profile.VideoCodec.ToLowerInvariant();
            var resolvedProfile = profile with { VideoCodec = resolvedCodec };
            var arguments = ScrcpyArgumentBuilder.Build(deviceSerial, resolvedProfile, windowTitle);
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
                DateTimeOffset.UtcNow,
                ProfileName: resolvedProfile.Name,
                VideoCodec: resolvedCodec,
                MaxSize: resolvedProfile.MaxSize,
                MaxFps: resolvedProfile.MaxFps,
                VideoBitRateMbps: resolvedProfile.VideoBitRateMbps,
                RecordPath: resolvedProfile.RecordPath);
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

    private async Task<string> ResolveCodecAsync(string serial, MirrorProfile profile, CancellationToken cancellationToken)
    {
        if (_codecCache.TryGetValue(serial, out var cached)) return cached;
        try
        {
            var result = await _commandRunner.RunAsync(new CommandRequest(
                scrcpyPath,
                [$"--serial={serial}", "--list-encoders"],
                Path.GetDirectoryName(scrcpyPath),
                Timeout: TimeSpan.FromSeconds(20)), cancellationToken).ConfigureAwait(false);
            var available = Regex.Matches($"{result.StandardOutput}\n{result.StandardError}", @"\b(h264|h265|av1|vp9|vp8)\b", RegexOptions.IgnoreCase)
                .Select(match => match.Value.ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selected = profile.Id == MirrorProfile.Quality.Id && available.Contains("h265")
                ? "h265"
                : new[] { "h264", "h265", "av1", "vp9", "vp8" }.FirstOrDefault(available.Contains) ?? "h264";
            _codecCache[serial] = selected;
            return selected;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return "h264";
        }
    }

    public async Task<int> ArrangeWindowsAsync(MirrorWindowLayout layout, CancellationToken cancellationToken = default)
    {
        var processes = _sessions.Values.Where(session => IsRunning(session.Process)).Select(session => session.Process).ToArray();
        if (processes.Length == 0) return 0;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (processes.Any(process => process.MainWindowHandle == 0) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            foreach (var process in processes) process.Refresh();
        }

        NativeMethods.SystemParametersInfo(0x0030, 0, out var workArea, 0);
        var count = processes.Count(process => process.MainWindowHandle != 0);
        if (count == 0) return 0;
        var columns = layout switch
        {
            MirrorWindowLayout.Vertical => 1,
            MirrorWindowLayout.Horizontal => count,
            _ => (int)Math.Ceiling(Math.Sqrt(count))
        };
        var rows = (int)Math.Ceiling(count / (double)columns);
        var width = Math.Max(320, (workArea.Right - workArea.Left) / columns);
        var height = Math.Max(240, (workArea.Bottom - workArea.Top) / rows);
        var index = 0;
        foreach (var process in processes.Where(process => process.MainWindowHandle != 0))
        {
            var column = index % columns;
            var row = index / columns;
            NativeMethods.MoveWindow(process.MainWindowHandle, workArea.Left + column * width, workArea.Top + row * height, width, height, true);
            index++;
        }
        return count;
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

    private static partial class NativeMethods
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        internal struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool SystemParametersInfo(uint action, uint parameter, out Rect value, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool MoveWindow(nint window, int x, int y, int width, int height, bool repaint);
    }
}
