using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AdbMirrorStudio.Application.Adb;
using AdbMirrorStudio.Application.Devices;
using AdbMirrorStudio.Application.Diagnostics;
using AdbMirrorStudio.Application.Mirroring;
using AdbMirrorStudio.Application.Settings;
using AdbMirrorStudio.Domain.Devices;
using AdbMirrorStudio.Domain.Mirroring;
using AdbMirrorStudio.Domain.Settings;

namespace AdbMirrorStudio.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IAdbService _adb;
    private readonly IMirrorSessionManager _mirrorSessions;
    private readonly DeviceRefreshCoordinator _refreshCoordinator;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IDiagnosticsService _diagnosticsService;
    private readonly SynchronizationContext _uiContext;
    private CancellationTokenSource? _refreshCancellation;
    private CancellationTokenSource? _transferCancellation;
    private bool _isBusy;
    private string _statusText = "正在初始化设备服务…";
    private string _endpoint = "192.168.1.100:5555";
    private string _pairEndpoint = string.Empty;
    private string _pairingCode = string.Empty;
    private string _transferFilePath = string.Empty;
    private string _selectedMirrorProfileId = MirrorProfile.Balanced.Id;
    private string _recordingPath = string.Empty;
    private AppSettings _settings = AppSettings.Default;

    public MainViewModel(AppServices services)
    {
        _adb = services.Adb;
        _mirrorSessions = services.MirrorSessions;
        _settingsStore = services.Settings;
        _diagnosticsService = services.Diagnostics;
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
    public IReadOnlyList<MirrorProfile> MirrorProfiles { get; } = MirrorProfile.Presets;
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

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
    public string AppVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    public string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AdbMirrorStudio");

    public async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        Endpoint = _settings.LastEndpoint;
        SelectedMirrorProfileId = MirrorProfile.Presets.Any(profile => profile.Id == _settings.MirrorProfileId)
            ? _settings.MirrorProfileId
            : MirrorProfile.Balanced.Id;
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(AutoRefresh));
        OnPropertyChanged(nameof(AutoReconnect));
        await DiscoverAsync();
        await RefreshAsync();
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
        await _settingsStore.SaveAsync(_settings);
    }

    public async Task SetAutoRefreshAsync(bool enabled)
    {
        if (_settings.AutoRefresh == enabled) return;
        _settings = _settings with { AutoRefresh = enabled };
        OnPropertyChanged(nameof(AutoRefresh));
        await _settingsStore.SaveAsync(_settings);
        StatusText = enabled ? "已启用设备自动刷新（每 5 秒）" : "已关闭设备自动刷新";
    }

    public async Task SetAutoReconnectAsync(bool enabled)
    {
        if (_settings.AutoReconnect == enabled) return;
        _settings = _settings with { AutoReconnect = enabled };
        OnPropertyChanged(nameof(AutoReconnect));
        await _settingsStore.SaveAsync(_settings);
        StatusText = enabled ? "已启用历史无线设备自动重连" : "已关闭自动重连";
    }

    public async Task SetMirrorProfileAsync(string profileId)
    {
        if (!MirrorProfile.Presets.Any(profile => profile.Id == profileId)) return;
        SelectedMirrorProfileId = profileId;
        _settings = _settings with { MirrorProfileId = profileId };
        await _settingsStore.SaveAsync(_settings);
        StatusText = $"镜像预设已切换为 {MirrorProfile.Presets.First(profile => profile.Id == profileId).Name}";
    }

    public async Task CompleteFirstRunAsync()
    {
        if (_settings.FirstRunCompleted) return;
        _settings = _settings with { FirstRunCompleted = true };
        OnPropertyChanged(nameof(FirstRunCompleted));
        await _settingsStore.SaveAsync(_settings);
    }

    public async Task InstallApkAsync(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            StatusText = "请选择一台目标设备";
            return;
        }

        IsBusy = true;
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
            IsBusy = false;
        }
    }

    public async Task PushFileAsync(string? serial)
        => await PushFilesAsync(serial);

    public void SetTransferFiles(IEnumerable<string> paths)
    {
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

        _transferCancellation?.Dispose();
        _transferCancellation = new CancellationTokenSource();
        IsBusy = true;
        StatusText = $"正在向 {serial} 推送 {TransferQueue.Count} 个文件…";
        var completed = 0;
        var failed = 0;
        try
        {
            foreach (var item in TransferQueue)
            {
                _transferCancellation.Token.ThrowIfCancellationRequested();
                item.Status = "传输中";
                try
                {
                    await _adb.PushFileAsync(serial, item.Path, cancellationToken: _transferCancellation.Token);
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
            IsBusy = false;
        }
    }

    public void CancelTransfer()
    {
        _transferCancellation?.Cancel();
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

        IsBusy = true;
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
            IsBusy = false;
        }
    }

    public async Task DisconnectAsync(string serial)
    {
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
    }

    public async Task RebootAsync(string serial)
    {
        IsBusy = true;
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
            IsBusy = false;
        }
    }

    public async Task EnableTcpIpAsync(string serial, int port)
    {
        IsBusy = true;
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
            IsBusy = false;
        }
    }

    public async Task RefreshAsync()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();
        IsBusy = true;
        StatusText = "正在刷新设备…";

        try
        {
            var snapshot = await _refreshCoordinator.RefreshAsync(_refreshCancellation.Token);
            if (snapshot is null) return;

            var running = _mirrorSessions.ActiveSessions.Select(session => session.DeviceSerial).ToHashSet(StringComparer.Ordinal);
            Devices.Clear();
            foreach (var device in snapshot.Devices)
            {
                Devices.Add(new DeviceCardViewModel(device, running.Contains(device.Serial)));
            }

            StatusText = snapshot.Devices.Count == 0
                ? "未发现设备，可通过 USB 或无线地址连接"
                : $"已发现 {snapshot.Devices.Count} 台设备";
            OnPropertyChanged(nameof(OnlineSummary));
        }
        catch (OperationCanceledException)
        {
            StatusText = "刷新已取消";
        }
        catch (Exception exception)
        {
            StatusText = $"刷新失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ConnectAsync()
    {
        IsBusy = true;
        StatusText = $"正在连接 {Endpoint}…";
        try
        {
            var result = await _adb.ConnectAsync(Endpoint);
            StatusText = result;
            _settings = _settings with { LastEndpoint = Endpoint.Trim(), HasConnectedBefore = true };
            await _settingsStore.SaveAsync(_settings);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            StatusText = $"连接失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task StartMirrorAsync(string serial)
    {
        StatusText = $"正在启动 {serial} 的镜像…";
        try
        {
            var card = Devices.FirstOrDefault(device => device.Serial == serial);
            var profile = MirrorProfile.Presets.FirstOrDefault(item => item.Id == SelectedMirrorProfileId)
                ?? MirrorProfile.Balanced;
            if (IsRecordingEnabled) profile = profile with { RecordPath = RecordingPath };
            await _mirrorSessions.StartAsync(serial, profile, card?.DisplayName);
            StatusText = IsRecordingEnabled
                ? $"已启动 {serial} 的镜像并录制到 {RecordingPath}"
                : $"已使用“{profile.Name}”预设启动 {serial} 的镜像";
        }
        catch (Exception exception)
        {
            StatusText = $"镜像启动失败：{exception.Message}";
        }
    }

    public async Task StopMirrorAsync(string serial)
    {
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
    }

    public async Task RunDiagnosticsAsync()
    {
        IsBusy = true;
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
            IsBusy = false;
        }
    }

    private void OnSessionChanged(object? sender, MirrorSession session)
    {
        _uiContext.Post(_ =>
        {
            var card = Devices.FirstOrDefault(device => device.Serial == session.DeviceSerial);
            if (card is not null) card.IsMirroring = session.State == MirrorSessionState.Running;
            var existing = Sessions.FirstOrDefault(item => item.DeviceSerial == session.DeviceSerial);
            if (existing is not null) Sessions.Remove(existing);
            if (session.State is MirrorSessionState.Starting or MirrorSessionState.Running or MirrorSessionState.Stopping)
            {
                Sessions.Add(new MirrorSessionCardViewModel(session));
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
