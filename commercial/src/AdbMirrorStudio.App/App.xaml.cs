using AdbMirrorStudio.Application.Adb;
using AdbMirrorStudio.Application.Diagnostics;
using AdbMirrorStudio.Application.Mirroring;
using AdbMirrorStudio.Application.Settings;
using AdbMirrorStudio.Infrastructure.Adb;
using AdbMirrorStudio.Infrastructure.Diagnostics;
using AdbMirrorStudio.Infrastructure.Processes;
using AdbMirrorStudio.Infrastructure.Persistence;
using AdbMirrorStudio.Infrastructure.Scrcpy;
using Microsoft.UI.Xaml;

namespace AdbMirrorStudio.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private MainWindow? _window;
    private IMirrorSessionManager? _mirrorSessions;
    internal MainWindow? MainWindow => _window;

    public App()
    {
        UnhandledException += (_, eventArgs) => CrashLog.Write(eventArgs.Exception);
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var toolsDirectory = Path.Combine(AppContext.BaseDirectory, "Tools");
            var runner = new ProcessCommandRunner();
            var adbPath = Path.Combine(toolsDirectory, "adb.exe");
            var scrcpyPath = Path.Combine(toolsDirectory, "scrcpy.exe");
            IAdbService adb = new AdbService(runner, adbPath);
            _mirrorSessions = new MirrorSessionManager(adb, scrcpyPath);
            IDiagnosticsService diagnostics = new DiagnosticsService(runner, adbPath, scrcpyPath);
            IAppSettingsStore settings = new JsonAppSettingsStore(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AdbMirrorStudio",
                "settings.json"));

            _window = new MainWindow(new AppServices(adb, _mirrorSessions, settings, diagnostics));
            _window.Closed += OnWindowClosed;
            _window.Activate();
        }
        catch (Exception exception)
        {
            CrashLog.Write(exception);
            throw;
        }
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_mirrorSessions is not null) await _mirrorSessions.DisposeAsync();
    }
}

public sealed record AppServices(
    IAdbService Adb,
    IMirrorSessionManager MirrorSessions,
    IAppSettingsStore Settings,
    IDiagnosticsService Diagnostics);

internal static class CrashLog
{
    public static void Write(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AdbMirrorStudio",
                "Crash");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, $"crash-{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}.log"),
                exception.ToString());
        }
        catch
        {
            // Crash reporting must never mask the original failure.
        }
    }
}
