using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AdbMirrorStudio.Application.Adb;
using AdbMirrorStudio.Application.Devices;
using AdbMirrorStudio.Application.Diagnostics;
using AdbMirrorStudio.Application.Mirroring;
using AdbMirrorStudio.Application.Settings;
using AdbMirrorStudio.Application.Updates;
using AdbMirrorStudio.Domain.Devices;
using AdbMirrorStudio.Domain.Mirroring;
using AdbMirrorStudio.Domain.Settings;

namespace AdbMirrorStudio.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAdbService _adb;
    private readonly IMirrorSessionManager _mirrorSessions;
    private readonly DeviceRefreshCoordinator _refreshCoordinator;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IDiagnosticsService _diagnosticsService;
    private readonly IUpdateService _updates;
    private readonly SynchronizationContext _uiContext;
    private CancellationTokenSource? _refreshCancellation;
    private CancellationTokenSource? _transferCancellation;
    private int _busyCount;
    private int _transferRunning;
    private bool _disposed;
    private bool _isBusy;
    private string _statusText = "正在初始化设备服务…";
    private string _endpoint = "192.168.1.100:5555";
    private string _pairEndpoint = string.Empty;
    private string _pairingCode = string.Empty;
    private string _transferFilePath = string.Empty;
    private string _selectedMirrorProfileId = MirrorProfile.Balanced.Id;
    private string _recordingPath = string.Empty;
    private string _updateStatusText = "尚未检查更新";
    private string? _updateDownloadUrl;
    private bool _updateAvailable;
    private string _remoteFilePath = "/sdcard/Download/";
    private string _localDownloadDirectory = string.Empty;
    private string? _selectedTransferDeviceSerial;
    private string? _selectedToolsDeviceSerial;
    private string? _selectedAppPackage;
    private AppSettings _settings = AppSettings.Default;

    public MainViewModel(AppServices services)
    {
        _adb = services.Adb;
        _mirrorSessions = services.MirrorSessions;
        _settingsStore = services.Settings;
        _diagnosticsService = services.Diagnostics;
        _updates = services.Updates;
        _refreshCoordinator = new DeviceRefreshCoordinator(_adb);
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("MainViewModel 必须在 UI 线程创建。");
        _mirrorSessions.SessionChanged += OnSessionChanged;
    }

    public ObservableCollection<DeviceCardViewModel> Devices { get; } = [];
    public ObservableCollection<string> DiscoveredPairingEndpoints { get; } = [];
    public ObservableCollection<MirrorSessionCardViewModel> Sessions { get; } = [];
    public ObservableCollection<DiagnosticItemViewModel> Diagnostics { get; } = [];
    public ObservableCollection<TransferItemViewModel> TransferQueue { get; } = [];
    public ObservableCollection<InstalledAppViewModel> InstalledApps { get; } = [];
    public ObservableCollection<RecordingItemViewModel> Recordings { get; } = [];
    public IReadOnlyList<MirrorProfile> MirrorProfiles { get; } = MirrorProfile.Presets;
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value)) OnPropertyChanged(nameof(IsIdle));
        }
    }
    public bool IsIdle => !IsBusy;
    public bool IsTransferRunning => Volatile.Read(ref _transferRunning) != 0;

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string Endpoint
    {
        get => _endpoint;
        set => SetField(ref _endpoint, value);
    }

    public string PairEndpoint
    {
        get => _pairEndpoint;
        set => SetField(ref _pairEndpoint, value);
    }

    public string PairingCode
    {
        get => _pairingCode;
        set => SetField(ref _pairingCode, value);
    }

    public string TransferFilePath
    {
        get => _transferFilePath;
        set => SetField(ref _transferFilePath, value);
    }

    public string SelectedMirrorProfileId
    {
        get => _selectedMirrorProfileId;
        set => SetField(ref _selectedMirrorProfileId, value);
    }

    public string RecordingPath
    {
        get => _recordingPath;
        set
        {
            if (SetField(ref _recordingPath, value)) OnPropertyChanged(nameof(IsRecordingEnabled));
        }
    }

    public bool IsRecordingEnabled => !string.IsNullOrWhiteSpace(RecordingPath);

    public string OnlineSummary => $"{Devices.Count(device => device.State == DeviceState.Online)} 台在线";
    public string Theme => _settings.Theme;
    public bool AutoRefresh => _settings.AutoRefresh;
    public bool AutoReconnect => _settings.AutoReconnect;
    public bool FirstRunCompleted => _settings.FirstRunCompleted;
    public string AppVersion => $"V{typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";
    public string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AdbMirrorStudio");
    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => SetField(ref _updateStatusText, value);
    }
    public string? UpdateDownloadUrl
    {
        get => _updateDownloadUrl;
        private set => SetField(ref _updateDownloadUrl, value);
    }
    public bool UpdateAvailable
    {
        get => _updateAvailable;
        private set => SetField(ref _updateAvailable, value);
    }
    public string RemoteFilePath
    {
        get => _remoteFilePath;
        set => SetField(ref _remoteFilePath, value);
    }
    public string LocalDownloadDirectory
    {
        get => _localDownloadDirectory;
        set => SetField(ref _localDownloadDirectory, value);
    }
    public string? SelectedTransferDeviceSerial
    {
        get => _selectedTransferDeviceSerial;
        set => SetField(ref _selectedTransferDeviceSerial, value);
    }
    public string? SelectedToolsDeviceSerial
    {
        get => _selectedToolsDeviceSerial;
        set => SetField(ref _selectedToolsDeviceSerial, value);
    }
    public string? SelectedAppPackage
    {
        get => _selectedAppPackage;
        set => SetField(ref _selectedAppPackage, value);
    }

    public async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        if (_disposed) return;
        Endpoint = _settings.LastEndpoint;
        SelectedMirrorProfileId = MirrorProfile.Presets.Any(profile => profile.Id == _settings.MirrorProfileId)
            ? _settings.MirrorProfileId
            : MirrorProfile.Balanced.Id;
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(AutoRefresh));
        OnPropertyChanged(nameof(AutoReconnect));
        await DiscoverAsync();
        if (_disposed) return;
        await RefreshAsync();
        if (_disposed) return;
        if (_settings.AutoReconnect && _settings.HasConnectedBefore && Devices.All(device => device.Serial != Endpoint))
        {
            await ConnectAsync();
        }
    }

    public async Task SetThemeAsync(string theme)
    {
        if (theme is not ("System" or "Light" or "Dark"))
        {
            throw new ArgumentOutOfRangeException(nameof(theme));
        }

        _settings = _settings with { Theme = theme };
        OnPropertyChanged(nameof(Theme));
        await SaveSettingsSafelyAsync();
    }

    public async Task SetAutoRefreshAsync(bool enabled)
    {
        if (_settings.AutoRefresh == enabled) return;
        _settings = _settings with { AutoRefresh = enabled };
        OnPropertyChanged(nameof(AutoRefresh));
        await SaveSettingsSafelyAsync();
        StatusText = enabled ? "已启用设备自动刷新（每 5 秒）" : "已关闭设备自动刷新";
    }

    public async Task SetAutoReconnectAsync(bool enabled)
    {
        if (_settings.AutoReconnect == enabled) return;
        _settings = _settings with { AutoReconnect = enabled };
        OnPropertyChanged(nameof(AutoReconnect));
        await SaveSettingsSafelyAsync();
        StatusText = enabled ? "已启用历史无线设备自动重连" : "已关闭自动重连";
    }

    public async Task SetMirrorProfileAsync(string profileId)
    {
        if (!MirrorProfile.Presets.Any(profile => profile.Id == profileId)) return;
        SelectedMirrorProfileId = profileId;
        _settings = _settings with { MirrorProfileId = profileId };
        await SaveSettingsSafelyAsync();
        StatusText = $"镜像预设已切换为 {MirrorProfile.Presets.First(profile => profile.Id == profileId).Name}";
    }

    public async Task CompleteFirstRunAsync()
    {
        if (_settings.FirstRunCompleted) return;
        _settings = _settings with { FirstRunCompleted = true };
        OnPropertyChanged(nameof(FirstRunCompleted));
        await SaveSettingsSafelyAsync();
    }

    public async Task InstallApkAsync(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            StatusText = "请选择一台目标设备";
            return;
        }

        EnterBusy();
        StatusText = $"正在向 {serial} 安装 APK…";
        try
        {
            var result = await _adb.InstallApkAsync(serial, TransferFilePath);
            StatusText = string.IsNullOrWhiteSpace(result) ? "APK 安装完成" : $"APK 安装完成：{result}";
        }
        catch (Exception exception)
        {
            StatusText = $"APK 安装失败：{exception.Message}";
        }
        finally
        {
            ExitBusy();
        }
    }

    public async Task PushFileAsync(string? serial)
        => await PushFilesAsync(serial);

    public void SetTransferFiles(IEnumerable<string> paths)
    {
        if (IsTransferRunning)
        {
            StatusText = "文件传输进行中，完成或取消后才能更改队列";
            return;
        }
        var files = paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        TransferQueue.Clear();
        foreach (var path in files) TransferQueue.Add(new TransferItemViewModel(path));
        TransferFilePath = files.FirstOrDefault() ?? string.Empty;
        StatusText = files.Length == 0 ? "未选择有效文件" : $"已加入 {files.Length} 个文件";
    }

    public async Task PushFilesAsync(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            StatusText = "请选择一台目标设备";
            return;
        }

        if (TransferQueue.Count == 0 && File.Exists(TransferFilePath))
        {
            TransferQueue.Add(new TransferItemViewModel(TransferFilePath));
        }
        if (TransferQueue.Count == 0)
        {
            StatusText = "请选择或拖入至少一个文件";
            return;
        }

        if (Interlocked.CompareExchange(ref _transferRunning, 1, 0) != 0)
        {
            StatusText = "已有文件传输任务正在运行";
            return;
        }
        OnPropertyChanged(nameof(IsTransferRunning));

        var transferCancellation = new CancellationTokenSource();
        _transferCancellation = transferCancellation;
        EnterBusy();
        StatusText = $"正在向 {serial} 推送 {TransferQueue.Count} 个文件…";
        var completed = 0;
        var failed = 0;
        try
        {
            foreach (var item in TransferQueue)
            {
                transferCancellation.Token.ThrowIfCancellationRequested();
                item.Status = "传输中";
                try
                {
                    await _adb.PushFileAsync(serial, item.Path, cancellationToken: transferCancellation.Token);
                    item.Status = "已完成";
                    item.IsComplete = true;
                    completed++;
                }
                catch (OperationCanceledException)
                {
                    item.Status = "已取消";
                    throw;
                }
                catch (Exception exception)
                {
                    item.Status = $"失败：{exception.Message}";
                    failed++;
                }
            }
            StatusText = $"文件任务完成：{completed} 个成功，{failed} 个失败";
        }
        catch (OperationCanceledException)
        {
            foreach (var item in TransferQueue.Where(item => !item.IsComplete && item.Status == "等待中"))
            {
                item.Status = "已取消";
            }
            StatusText = $"传输已取消，已完成 {completed} 个文件";
        }
        catch (Exception exception)
        {
            StatusText = $"文件推送失败：{exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_transferCancellation, transferCancellation)) _transferCancellation = null;
            transferCancellation.Dispose();
            Interlocked.Exchange(ref _transferRunning, 0);
            OnPropertyChanged(nameof(IsTransferRunning));
            ExitBusy();
        }
    }

    public void CancelTransfer()
    {
        if (_transferCancellation is null)
        {
            StatusText = "当前没有文件传输任务";
            return;
        }
        _transferCancellation.Cancel();
        StatusText = "正在取消文件传输…";
    }

    public async Task DiscoverAsync()
    {
        try
        {
            var services = await _adb.DiscoverAsync();
            DiscoveredPairingEndpoints.Clear();
            foreach (var endpoint in services
                         .Where(service => service.ServiceType.Contains("pairing", StringComparison.OrdinalIgnoreCase))
                         .Select(service => service.Endpoint)
                         .Distinct(StringComparer.Ordinal))
            {
                DiscoveredPairingEndpoints.Add(endpoint);
            }
            if (string.IsNullOrWhiteSpace(PairEndpoint) && DiscoveredPairingEndpoints.Count > 0)
            {
                PairEndpoint = DiscoveredPairingEndpoints[0];
            }
        }
        catch
        {
            // mDNS is optional; manual pairing remains available.
        }
    }

    public async Task PairAsync()
    {
        if (string.IsNullOrWhiteSpace(PairEndpoint) || string.IsNullOrWhiteSpace(PairingCode))
        {
            StatusText = "请输入配对地址和手机显示的配对码";
            return;
        }

        EnterBusy();
        StatusText = $"正在配对 {PairEndpoint}…";
        try
        {
            var result = await _adb.PairAsync(PairEndpoint, PairingCode);
            StatusText = result;
            await DiscoverAsync();
        }
        catch (Exception exception)
        {
            StatusText = $"配对失败：{exception.Message}";
        }
        finally
        {
            PairingCode = string.Empty;
            ExitBusy();
        }
    }

    public async Task DisconnectAsync(string serial)
    {
        EnterBusy();
        StatusText = $"正在断开 {serial}…";
        try
        {
            await _adb.DisconnectAsync(serial);
            StatusText = $"已断开 {serial}";
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            StatusText = $"断开失败：{exception.Message}";
        }
        finally
        {
            ExitBusy();
        }
    }

    public async Task RebootAsync(string serial)
    {
        EnterBusy();
        StatusText = $"正在重启 {serial}…";
        try
        {
            await _adb.RebootAsync(serial);
            StatusText = $"已向 {serial} 发送重启命令，设备重新上线可能需要约一分钟";
        }
        catch (Exception exception)
        {
            StatusText = $"重启失败：{exception.Message}";
        }
        finally
        {
            ExitBusy();
        }
    }

    public async Task EnableTcpIpAsync(string serial, int port)
    {
        EnterBusy();
        StatusText = $"正在让 {serial} 监听 TCP/IP 端口 {port}…";
        try
        {
            var result = await _adb.EnableTcpIpAsync(serial, port);
            StatusText = string.IsNullOrWhiteSpace(result)
                ? $"设备已切换到 TCP/IP 端口 {port}"
                : result;
        }
        catch (Exception exception)
        {
            StatusText = $"TCP/IP 切换失败：{exception.Message}";
        }
        finally
        {
            ExitBusy();
        }
    }

    public async Task RefreshAsync()
    {
        var refreshCancellation = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(ref _refreshCancellation, refreshCancellation);
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();
        EnterBusy();
        StatusText = "正在刷新设备…";

        try
        {
            var snapshot = await _refreshCoordinator.RefreshAsync(refreshCancellation.Token);
            if (snapshot is null) return;

            var activeSessions = _mirrorSessions.ActiveSessions
                .OrderByDescending(session => session.StartedAt)
                .ToArray();
            var running = activeSessions.Select(session => session.DeviceSerial).ToHashSet(StringComparer.Ordinal);
            var selectedTransfer = SelectedTransferDeviceSerial;
            var selectedTools = SelectedToolsDeviceSerial;
            Devices.Clear();
            foreach (var device in snapshot.Devices)
            {
                Devices.Add(new DeviceCardViewModel(device, running.Contains(device.Serial)));
            }

            Sessions.Clear();
            foreach (var session in activeSessions.Where(session => session.State is MirrorSessionState.Starting or MirrorSessionState.Running or MirrorSessionState.Stopping))
            {
                Sessions.Add(new MirrorSessionCardViewModel(session));
            }

            foreach (var recordingSession in activeSessions.Where(session => !string.IsNullOrWhiteSpace(session.RecordPath)))
            {
                var existing = Recordings.FirstOrDefault(item => item.SessionId == recordingSession.Id);
                if (existing is null)
                {
                    Recordings.Insert(0, new RecordingItemViewModel(recordingSession));
                }
                else
                {
                    existing.Update(recordingSession);
                }
            }

            var onlineSerials = snapshot.Devices
                .Where(device => device.State == DeviceState.Online)
                .Select(device => device.Serial)
                .ToHashSet(StringComparer.Ordinal);
            var fallbackSerial = snapshot.Devices.FirstOrDefault(device => device.State == DeviceState.Online)?.Serial;
            SelectedTransferDeviceSerial = selectedTransfer is not null && onlineSerials.Contains(selectedTransfer)
                ? selectedTransfer
                : fallbackSerial;
            SelectedToolsDeviceSerial = selectedTools is not null && onlineSerials.Contains(selectedTools)
                ? selectedTools
                : fallbackSerial;

            StatusText = snapshot.Devices.Count == 0
                ? "未发现设备，可通过 USB 或无线地址连接"
                : $"已发现 {snapshot.Devices.Count} 台设备";
            OnPropertyChanged(nameof(OnlineSummary));
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(Volatile.Read(ref _refreshCancellation), refreshCancellation))
            {
                StatusText = "刷新已取消";
            }
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(Volatile.Read(ref _refreshCancellation), refreshCancellation))
            {
                StatusText = $"刷新失败：{exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _refreshCancellation, null, refreshCancellation), refreshCancellation))
            {
                refreshCancellation.Dispose();
            }
            ExitBusy();
        }
    }

    public async Task ConnectAsync()
    {
        EnterBusy();
        StatusText = $"正在连接 {Endpoint}…";
        try
        {
            var result = await _adb.ConnectAsync(Endpoint);
            StatusText = result;
            _settings = _settings with { LastEndpoint = Endpoint.Trim(), HasConnectedBefore = true };
            await SaveSettingsSafelyAsync();
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            StatusText = $"连接失败：{exception.Message}";
        }
        finally
        {
            ExitBusy();
        }
    }

    public async Task StartMirrorAsync(string serial)
    {
        EnterBusy();
        StatusText = $"正在启动 {serial} 的镜像…";
        try
        {
            var card = Devices.FirstOrDefault(device => device.Serial == serial);
            var profile = MirrorProfile.Presets.FirstOrDefault(item => item.Id == SelectedMirrorProfileId)
                ?? MirrorProfile.Balanced;
            if (IsRecordingEnabled) profile = profile with { RecordPath = RecordingPath };
            var session = await _mirrorSessions.StartAsync(serial, profile, card?.DisplayName);
            StatusText = !string.IsNullOrWhiteSpace(session.RecordPath)
                ? $"已启动 {serial} 的镜像并录制到 {session.RecordPath}"
                : $"已使用“{session.ProfileName}”预设启动 {serial} 的镜像";
        }
        catch (Exception exception)
        {
            StatusText = $"镜像启动失败：{exception.Message}";
        }
        finally
        {
            ExitBusy();
        }
    }

    public async Task StopMirrorAsync(string serial)
    {
        EnterBusy();
        StatusText = $"正在停止 {serial} 的镜像…";
        try
        {
            await _mirrorSessions.StopAsync(serial);
            StatusText = $"已停止 {serial} 的镜像";
        }
        catch (Exception exception)
        {
            StatusText = $"停止镜像失败：{exception.Message}";
        }
        finally
        {
            ExitBusy();
        }
    }

    public async Task RunDiagnosticsAsync()
    {
        EnterBusy();
        StatusText = "正在运行环境诊断…";
        try
        {
            var results = await _diagnosticsService.RunAsync();
            Diagnostics.Clear();
            foreach (var result in results)
            {
                Diagnostics.Add(new DiagnosticItemViewModel(result));
            }

            var errors = results.Count(item => item.Severity == DiagnosticSeverity.Error);
            var warnings = results.Count(item => item.Severity == DiagnosticSeverity.Warning);
            StatusText = errors > 0
                ? $"诊断完成：{errors} 项错误，{warnings} 项警告"
                : $"诊断完成：没有错误，{warnings} 项警告";
        }
        catch (OperationCanceledException)
        {
            StatusText = "诊断已取消";
        }
        catch (Exception exception)
        {
            StatusText = $"诊断失败：{exception.Message}";
        }
        finally
        {
            ExitBusy();
        }
    }

    public async Task CheckForUpdatesAsync()
    {
        EnterBusy();
        UpdateStatusText = "正在检查 GitHub Release…";
        try
        {
            var update = await _updates.CheckAsync();
            UpdateAvailable = update.IsUpdateAvailable;
            UpdateDownloadUrl = update.DownloadUrl ?? update.ReleaseUrl;
            UpdateStatusText = update.IsUpdateAvailable
                ? $"发现新版本 {update.LatestVersion}，当前版本 {update.CurrentVersion}"
                : $"当前已是最新版本 {update.CurrentVersion}";
        }
        catch (Exception exception)
        {
            UpdateAvailable = false;
            UpdateDownloadUrl = "https://github.com/Cuinings/ADB-Mirror-Studio/releases";
            UpdateStatusText = $"检查更新失败：{exception.Message}";
        }
        finally
        {
            ExitBusy();
        }
    }

    public async Task<DeviceDetails?> GetDeviceDetailsAsync(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            StatusText = "请选择一台目标设备";
            return null;
        }
        EnterBusy();
        StatusText = $"正在读取 {serial} 的设备信息…";
        try
        {
            var details = await _adb.GetDeviceDetailsAsync(serial);
            StatusText = $"已读取 {serial} 的设备信息";
            return details;
        }
        catch (Exception exception)
        {
            StatusText = $"读取设备信息失败：{exception.Message}";
            return null;
        }
        finally
        {
            ExitBusy();
        }
    }

    public async Task CaptureScreenshotAsync(string? serial, string localPath)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            StatusText = "请选择一台目标设备";
            return;
        }
        EnterBusy();
        StatusText = $"正在截取 {serial} 的屏幕…";
        try
        {
            var result = await _adb.CaptureScreenshotAsync(serial, localPath);
            StatusText = $"截图已保存到 {result}";
        }
        catch (Exception exception)
        {
            StatusText = $"截图失败：{exception.Message}";
        }
        finally
        {
            ExitBusy();
        }
    }

    public async Task ExportLogcatAsync(string? serial, string localPath)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            StatusText = "请选择一台目标设备";
            return;
        }
        EnterBusy();
        StatusText = $"正在导出 {serial} 的 Logcat…";
        try
        {
            var content = await _adb.GetLogcatSnapshotAsync(serial, 2000);
            await File.WriteAllTextAsync(localPath, content, System.Text.Encoding.UTF8);
            StatusText = $"Logcat 已保存到 {localPath}";
        }
        catch (Exception exception)
        {
            StatusText = $"Logcat 导出失败：{exception.Message}";
        }
        finally
        {
            ExitBusy();
        }
    }

    public async Task PullRemoteFileAsync(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            StatusText = "请选择一台目标设备";
            return;
        }
        EnterBusy();
        StatusText = $"正在从 {serial} 下载 {RemoteFilePath}…";
        try
        {
            var result = await _adb.PullFileAsync(serial, RemoteFilePath, LocalDownloadDirectory);
            StatusText = string.IsNullOrWhiteSpace(result) ? "设备文件下载完成" : $"下载完成：{result}";
        }
        catch (Exception exception)
        {
            StatusText = $"设备文件下载失败：{exception.Message}";
        }
        finally
        {
            ExitBusy();
        }
    }

    public async Task SendDeviceKeyAsync(int keyCode)
    {
        if (string.IsNullOrWhiteSpace(SelectedToolsDeviceSerial))
        {
            StatusText = "请选择一台目标设备";
            return;
        }
        try
        {
            await _adb.SendKeyEventAsync(SelectedToolsDeviceSerial, keyCode);
            StatusText = "设备控制指令已发送";
        }
        catch (Exception exception)
        {
            StatusText = $"设备控制失败：{exception.Message}";
        }
    }

    public async Task RefreshInstalledAppsAsync(bool includeSystemApps)
    {
        if (string.IsNullOrWhiteSpace(SelectedToolsDeviceSerial))
        {
            StatusText = "请选择一台目标设备";
            return;
        }
        EnterBusy();
        try
        {
            var apps = await _adb.GetInstalledAppsAsync(SelectedToolsDeviceSerial, includeSystemApps);
            InstalledApps.Clear();
            foreach (var app in apps) InstalledApps.Add(new InstalledAppViewModel(app));
            SelectedAppPackage = InstalledApps.FirstOrDefault()?.PackageName;
            StatusText = $"已读取 {apps.Count} 个应用";
        }
        catch (Exception exception)
        {
            StatusText = $"读取应用失败：{exception.Message}";
        }
        finally
        {
            ExitBusy();
        }
    }

    public async Task RunAppActionAsync(string action)
    {
        if (string.IsNullOrWhiteSpace(SelectedToolsDeviceSerial) || string.IsNullOrWhiteSpace(SelectedAppPackage))
        {
            StatusText = "请选择设备和应用";
            return;
        }
        EnterBusy();
        try
        {
            switch (action)
            {
                case "launch": await _adb.LaunchAppAsync(SelectedToolsDeviceSerial, SelectedAppPackage); break;
                case "stop": await _adb.ForceStopAppAsync(SelectedToolsDeviceSerial, SelectedAppPackage); break;
                case "uninstall": await _adb.UninstallAppAsync(SelectedToolsDeviceSerial, SelectedAppPackage); break;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
            StatusText = $"应用操作已完成：{SelectedAppPackage}";
            if (action == "uninstall") await RefreshInstalledAppsAsync(includeSystemApps: false);
        }
        catch (Exception exception)
        {
            StatusText = $"应用操作失败：{exception.Message}";
        }
        finally
        {
            ExitBusy();
        }
    }

    public async Task ArrangeMirrorWindowsAsync(MirrorWindowLayout layout)
    {
        try
        {
            var count = await _mirrorSessions.ArrangeWindowsAsync(layout);
            StatusText = count == 0 ? "没有可排列的镜像窗口" : $"已排列 {count} 个镜像窗口";
        }
        catch (Exception exception)
        {
            StatusText = $"窗口排列失败：{exception.Message}";
        }
    }

    private async Task SaveSettingsSafelyAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText = $"设置暂时无法保存：{exception.Message}";
        }
    }

    private void EnterBusy()
    {
        if (Interlocked.Increment(ref _busyCount) == 1) IsBusy = true;
    }

    private void ExitBusy()
    {
        var remaining = Interlocked.Decrement(ref _busyCount);
        if (remaining <= 0)
        {
            Interlocked.Exchange(ref _busyCount, 0);
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mirrorSessions.SessionChanged -= OnSessionChanged;
        var refreshCancellation = Interlocked.Exchange(ref _refreshCancellation, null);
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        _transferCancellation?.Cancel();
    }

    private void OnSessionChanged(object? sender, MirrorSession session)
    {
        if (_disposed) return;
        _uiContext.Post(_ =>
        {
            if (_disposed) return;
            var card = Devices.FirstOrDefault(device => device.Serial == session.DeviceSerial);
            if (card is not null) card.IsMirroring = session.State == MirrorSessionState.Running;
            var existing = Sessions.FirstOrDefault(item => item.DeviceSerial == session.DeviceSerial);
            if (existing is not null) Sessions.Remove(existing);
            if (session.State is MirrorSessionState.Starting or MirrorSessionState.Running or MirrorSessionState.Stopping)
            {
                Sessions.Add(new MirrorSessionCardViewModel(session));
            }
            if (!string.IsNullOrWhiteSpace(session.RecordPath))
            {
                var recording = Recordings.FirstOrDefault(item => item.SessionId == session.Id);
                if (recording is null)
                {
                    recording = new RecordingItemViewModel(session);
                    Recordings.Insert(0, recording);
                }
                else
                {
                    recording.Update(session);
                }
            }
        }, null);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class DeviceCardViewModel(DeviceInfo device, bool isMirroring) : INotifyPropertyChanged
{
    private bool _isMirroring = isMirroring;
    public string Serial => device.Serial;
    public string DisplayName => device.DisplayName;
    public string Model => device.Model;
    public DeviceState State => device.State;
    public string StateLabel => device.State switch
    {
        DeviceState.Online => "在线",
        DeviceState.Offline => "离线",
        DeviceState.Unauthorized => "未授权",
        _ => device.State.ToString()
    };
    public string ConnectionLabel => device.ConnectionKind == ConnectionKind.TcpIp ? "Wi-Fi" : "USB";
    public bool IsOnline => device.State == DeviceState.Online;
    public string MirrorButtonText => IsMirroring ? "运行中" : "打开镜像";

    public bool IsMirroring
    {
        get => _isMirroring;
        set
        {
            if (_isMirroring == value) return;
            _isMirroring = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMirroring)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MirrorButtonText)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record MirrorSessionCardViewModel(MirrorSession Session)
{
    public string DeviceSerial => Session.DeviceSerial;
    public string StateLabel => Session.State switch
    {
        MirrorSessionState.Starting => "正在启动",
        MirrorSessionState.Running => "运行中",
        MirrorSessionState.Stopping => "正在停止",
        _ => Session.State.ToString()
    };
    public string StartedAtLabel => Session.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string PerformanceLabel => $"{Session.ProfileName} · {Session.VideoCodec.ToUpperInvariant()} · {Session.MaxSize}px · {Session.MaxFps} FPS · {Session.VideoBitRateMbps} Mbps";
    public string RecordingLabel => string.IsNullOrWhiteSpace(Session.RecordPath) ? "未录制" : $"录制：{System.IO.Path.GetFileName(Session.RecordPath)}";
}

public sealed record DiagnosticItemViewModel(DiagnosticItem Item)
{
    public string Title => Item.Title;
    public string Detail => Item.Detail;
    public string StateLabel => Item.Severity switch
    {
        DiagnosticSeverity.Success => "正常",
        DiagnosticSeverity.Warning => "警告",
        DiagnosticSeverity.Error => "错误",
        _ => "未知"
    };
    public string Glyph => Item.Severity switch
    {
        DiagnosticSeverity.Success => "\uE930",
        DiagnosticSeverity.Warning => "\uE7BA",
        DiagnosticSeverity.Error => "\uEA39",
        _ => "\uE946"
    };
}

public sealed record InstalledAppViewModel(InstalledApp App)
{
    public string PackageName => App.PackageName;
}

public sealed class RecordingItemViewModel : INotifyPropertyChanged
{
    private string _status;
    private string _sizeLabel = "—";
    public RecordingItemViewModel(MirrorSession session)
    {
        SessionId = session.Id;
        DeviceSerial = session.DeviceSerial;
        Path = session.RecordPath ?? string.Empty;
        StartedAt = session.StartedAt;
        _status = StateLabel(session.State);
        Update(session);
    }
    public string SessionId { get; }
    public string DeviceSerial { get; }
    public string Path { get; }
    public DateTimeOffset StartedAt { get; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public string StartedAtLabel => StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string SizeLabel { get => _sizeLabel; private set { if (_sizeLabel == value) return; _sizeLabel = value; PropertyChanged?.Invoke(this, new(nameof(SizeLabel))); } }
    public string Status { get => _status; private set { if (_status == value) return; _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); } }
    public void Update(MirrorSession session)
    {
        Status = StateLabel(session.State);
        if (session.State == MirrorSessionState.Exited)
        {
            try
            {
                if (!File.Exists(Path))
                {
                    Status = "文件未生成";
                    return;
                }
                var bytes = new FileInfo(Path).Length;
                SizeLabel = bytes < 1024 * 1024 ? $"{bytes / 1024d:F1} KB" : $"{bytes / 1024d / 1024d:F1} MB";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                SizeLabel = "暂不可用";
            }
        }
    }
    private static string StateLabel(MirrorSessionState state) => state switch
    {
        MirrorSessionState.Running => "录制中",
        MirrorSessionState.Stopping => "正在结束",
        MirrorSessionState.Exited => "已完成",
        MirrorSessionState.Failed => "失败",
        _ => "准备中"
    };
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class TransferItemViewModel : INotifyPropertyChanged
{
    private string _status = "等待中";
    private bool _isComplete;
    public TransferItemViewModel(string path)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        var length = new FileInfo(path).Length;
        SizeLabel = length switch
        {
            < 1024 => $"{length} B",
            < 1024 * 1024 => $"{length / 1024d:F1} KB",
            _ => $"{length / 1024d / 1024d:F1} MB"
        };
    }
    public string Path { get; }
    public string FileName { get; }
    public string SizeLabel { get; }
    public string Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }
    public bool IsComplete
    {
        get => _isComplete;
        set => _isComplete = value;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
