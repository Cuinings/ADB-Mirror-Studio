using AdbMirrorStudio.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using Windows.Storage;
using Windows.ApplicationModel.DataTransfer;
using WinRT.Interop;
using System.Diagnostics;

namespace AdbMirrorStudio.App;

public sealed partial class MainPage : Page
{
    private readonly DispatcherTimer _autoRefreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _initializingSettings;
    public MainViewModel? ViewModel { get; private set; }

    public MainPage()
    {
        InitializeComponent();
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
        _autoRefreshTimer.Start();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not AppServices services) return;
        _initializingSettings = true;
        ViewModel = new MainViewModel(services);
        DataContext = ViewModel;
        await ViewModel.InitializeAsync();
        ApplyTheme(ViewModel.Theme, updateSelector: true);
        AutoRefreshToggle.IsOn = ViewModel.AutoRefresh;
        AutoReconnectToggle.IsOn = ViewModel.AutoReconnect;
        _initializingSettings = false;
        if (!ViewModel.FirstRunCompleted) await ShowFirstRunDialogAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.RefreshAsync();
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.ConnectAsync();
    }

    private async void Mirror_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not Button { CommandParameter: string serial }) return;
        await ViewModel.StartMirrorAsync(serial);
    }

    private void PairingCodeBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null && sender is PasswordBox passwordBox)
        {
            ViewModel.PairingCode = passwordBox.Password;
        }
    }

    private async void Pair_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        await ViewModel.PairAsync();
        PairingCodeBox.Password = string.Empty;
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not Button { CommandParameter: string serial }) return;
        await ViewModel.DisconnectAsync(serial);
    }

    private async void Reboot_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not Button { CommandParameter: string serial }) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "确认重启设备",
            Content = $"设备 {serial} 将立即重新启动，当前镜像和传输会中断。",
            PrimaryButtonText = "重启",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.RebootAsync(serial);
    }

    private async void EnableTcpIp_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not Button { CommandParameter: string serial }) return;
        var portBox = new NumberBox
        {
            Header = "监听端口",
            Value = 5555,
            Minimum = 1,
            Maximum = 65535,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "启用 ADB TCP/IP",
            Content = portBox,
            PrimaryButtonText = "启用",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !double.IsNaN(portBox.Value))
        {
            await ViewModel.EnableTcpIpAsync(serial, (int)portBox.Value);
        }
    }

    private async void StopMirror_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not Button { CommandParameter: string serial }) return;
        await ViewModel.StopMirrorAsync(serial);
    }

    private async void RunDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.RunDiagnosticsAsync();
    }

    private async void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        DeviceCenterView.Visibility = Visibility.Collapsed;
        SessionsView.Visibility = Visibility.Collapsed;
        DiagnosticsView.Visibility = Visibility.Collapsed;
        FilesView.Visibility = Visibility.Collapsed;
        AboutView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;

        if (args.IsSettingsSelected)
        {
            SettingsView.Visibility = Visibility.Visible;
            return;
        }

        var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        switch (tag)
        {
            case "pairing":
                DeviceCenterView.Visibility = Visibility.Visible;
                PairingExpander.IsExpanded = true;
                break;
            case "sessions":
                SessionsView.Visibility = Visibility.Visible;
                break;
            case "diagnostics":
                DiagnosticsView.Visibility = Visibility.Visible;
                if (ViewModel is not null && ViewModel.Diagnostics.Count == 0)
                {
                    await ViewModel.RunDiagnosticsAsync();
                }
                break;
            case "files":
                FilesView.Visibility = Visibility.Visible;
                if (TransferDeviceSelector.SelectedIndex < 0 && TransferDeviceSelector.Items.Count > 0)
                {
                    TransferDeviceSelector.SelectedIndex = 0;
                }
                break;
            case "about":
                AboutView.Visibility = Visibility.Visible;
                break;
            default:
                DeviceCenterView.Visibility = Visibility.Visible;
                break;
        }
    }

    private async void ChooseApk_Click(object sender, RoutedEventArgs e) =>
        await ChooseFileAsync([".apk"]);

    private async void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || Microsoft.UI.Xaml.Application.Current is not App { MainWindow: not null } app) return;
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(app.MainWindow));
        var files = await picker.PickMultipleFilesAsync();
        ViewModel.SetTransferFiles(files.Select(file => file.Path));
    }

    private async Task ChooseFileAsync(IReadOnlyList<string> filters)
    {
        if (ViewModel is null || Microsoft.UI.Xaml.Application.Current is not App { MainWindow: not null } app) return;
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads
        };
        foreach (var filter in filters) picker.FileTypeFilter.Add(filter);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(app.MainWindow));
        var file = await picker.PickSingleFileAsync();
        if (file is not null) ViewModel.TransferFilePath = file.Path;
    }

    private async void InstallApk_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.InstallApkAsync(TransferDeviceSelector.SelectedValue as string);
    }

    private async void PushFile_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.PushFileAsync(TransferDeviceSelector.SelectedValue as string);
    }

    private void CancelTransfer_Click(object sender, RoutedEventArgs e) => ViewModel?.CancelTransfer();

    private void FileDrop_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "添加到传输队列";
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private async void FileDrop_Drop(object sender, DragEventArgs e)
    {
        if (ViewModel is null || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        ViewModel.SetTransferFiles(items.OfType<StorageFile>().Select(file => file.Path));
    }

    private async void MirrorProfileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingSettings || ViewModel is null || sender is not ComboBox { SelectedValue: string profileId }) return;
        await ViewModel.SetMirrorProfileAsync(profileId);
    }

    private async void ChooseRecordingPath_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || Microsoft.UI.Xaml.Application.Current is not App { MainWindow: not null } app) return;
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
            SuggestedFileName = $"adb-mirror-{DateTime.Now:yyyyMMdd-HHmmss}"
        };
        picker.FileTypeChoices.Add("Matroska 视频", [".mkv"]);
        picker.FileTypeChoices.Add("MP4 视频", [".mp4"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(app.MainWindow));
        var file = await picker.PickSaveFileAsync();
        if (file is not null) ViewModel.RecordingPath = file.Path;
    }

    private void ClearRecordingPath_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) ViewModel.RecordingPath = string.Empty;
    }

    private async void AutoRefreshToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializingSettings || ViewModel is null || sender is not ToggleSwitch toggle) return;
        await ViewModel.SetAutoRefreshAsync(toggle.IsOn);
    }

    private async void AutoReconnectToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializingSettings || ViewModel is null || sender is not ToggleSwitch toggle) return;
        await ViewModel.SetAutoReconnectAsync(toggle.IsOn);
    }

    private async void AutoRefreshTimer_Tick(object? sender, object e)
    {
        if (ViewModel is { AutoRefresh: true, IsBusy: false }) await ViewModel.RefreshAsync();
    }

    private async Task ShowFirstRunDialogAsync()
    {
        if (ViewModel is null) return;
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "开始前请确认以下事项：",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = "• 本应用通过 ADB 控制你主动授权的 Android 设备。\n• 首次连接必须在手机上确认 USB 调试授权。\n• 文件传输、APK 安装、重启和关闭屏幕只会在你点击相应操作后执行。\n• 设置和崩溃日志仅保存在本机，不会自动上传。",
            TextWrapping = TextWrapping.Wrap
        });
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "欢迎使用 ADB Mirror Studio",
            Content = content,
            PrimaryButtonText = "同意并开始",
            DefaultButton = ContentDialogButton.Primary
        };
        await dialog.ShowAsync();
        await ViewModel.CompleteFirstRunAsync();
    }

    private void OpenDataDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        Directory.CreateDirectory(ViewModel.DataDirectory);
        Process.Start(new ProcessStartInfo(ViewModel.DataDirectory) { UseShellExecute = true });
    }

    private async void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingSettings || ViewModel is null) return;
        var theme = ThemeSelector.SelectedIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "System"
        };
        ApplyTheme(theme, updateSelector: false);
        await ViewModel.SetThemeAsync(theme);
    }

    private void ApplyTheme(string theme, bool updateSelector)
    {
        RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        if (updateSelector)
        {
            ThemeSelector.SelectedIndex = theme switch
            {
                "Light" => 1,
                "Dark" => 2,
                _ => 0
            };
        }
    }
}
