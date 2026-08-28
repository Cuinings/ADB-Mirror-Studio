using System.Xml.Linq;

namespace AdbMirrorStudio.UnitTests;

public sealed class MainPageMarkupTests
{
    private static readonly XNamespace Ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void PrimaryNavigationContainsOnlyFourTaskEntrances()
    {
        var navigation = Markup.Descendants(Ui + "NavigationView").Single();
        var menuItems = navigation.Element(Ui + "NavigationView.MenuItems")!
            .Elements(Ui + "NavigationViewItem")
            .Select(item => item.Attribute("Content")?.Value ?? throw new InvalidDataException("导航项缺少 Content。"))
            .ToArray();

        Assert.Equal(["设备", "镜像与录制", "文件传输", "设备工具"], menuItems);
    }

    [Fact]
    public void FileTransferOwnsUploadDownloadAndApkSections()
    {
        var files = ElementNamed("FilesView");
        var headers = files.Descendants(Ui + "TabViewItem")
            .Select(item => item.Attribute("Header")?.Value ?? throw new InvalidDataException("文件分区缺少 Header。"))
            .ToArray();

        Assert.Equal(["上传文件", "从设备下载", "安装 APK"], headers);
        Assert.DoesNotContain(
            ElementNamed("ToolsView").Descendants().Attributes("Text"),
            attribute => attribute.Value == "从设备下载");
    }

    [Fact]
    public void FileTransferDeclaresUploadDownloadAndApkCommands()
    {
        var clickHandlers = ElementNamed("FilesView").Descendants()
            .Select(element => (string?)element.Attribute("Click"))
            .Where(value => value is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("ChooseFile_Click", clickHandlers);
        Assert.Contains("PushFile_Click", clickHandlers);
        Assert.Contains("ChooseDownloadDirectory_Click", clickHandlers);
        Assert.Contains("PullRemoteFile_Click", clickHandlers);
        Assert.Contains("ChooseApk_Click", clickHandlers);
        Assert.Contains("InstallApk_Click", clickHandlers);
    }

    [Fact]
    public void TransferAndToolsUseSameSelectedDeviceBinding()
    {
        foreach (var selectorName in new[] { "TransferDeviceSelector", "ToolsDeviceSelector" })
        {
            var selector = ElementNamed(selectorName);
            Assert.Contains("SelectedDeviceSerial", (string?)selector.Attribute("SelectedValue"));
        }
    }

    [Fact]
    public void SettingsOwnsDiagnosticsUpdatesDataAndAbout()
    {
        var headers = ElementNamed("SettingsView").Descendants(Ui + "TabViewItem")
            .Select(item => item.Attribute("Header")?.Value ?? throw new InvalidDataException("设置分区缺少 Header。"))
            .ToArray();

        Assert.Equal(["外观与常规", "诊断", "更新与数据", "关于与许可"], headers);
    }

    [Fact]
    public void GlobalStatusProvidesCopyAndDiagnosticsActions()
    {
        var actions = Markup.Descendants(Ui + "MenuFlyoutItem")
            .Select(item => new
            {
                Text = (string?)item.Attribute("Text"),
                Click = (string?)item.Attribute("Click")
            })
            .ToArray();

        Assert.Contains(actions, action => action is { Text: "复制状态", Click: "CopyStatus_Click" });
        Assert.Contains(actions, action => action is { Text: "打开诊断", Click: "OpenDiagnostics_Click" });
    }

    [Fact]
    public void NavigationCodeHasNoRemovedPageBranches()
    {
        var code = File.ReadAllText(FixturePath("MainPage.xaml.cs"));

        Assert.Contains("case \"sessions\"", code, StringComparison.Ordinal);
        Assert.Contains("case \"files\"", code, StringComparison.Ordinal);
        Assert.Contains("case \"tools\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("case \"diagnostics\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("case \"about\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceCardShowsStatusImmediatelyAfterDeviceName()
    {
        var deviceName = Markup.Descendants(Ui + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "{Binding DisplayName}");
        var row = deviceName.Parent ?? throw new InvalidDataException("设备名称缺少容器。");
        var bindings = row.Descendants(Ui + "TextBlock")
            .Select(element => (string?)element.Attribute("Text")
                ?? throw new InvalidDataException("设备状态缺少 Text。"))
            .ToArray();

        Assert.Equal(
            ["{Binding DisplayName}", "{Binding ConnectionLabel}", "{Binding StateLabel}"],
            bindings);
    }

    [Fact]
    public void DeviceCardExposesAllActionsWithoutMoreMenu()
    {
        var clickHandlers = Markup.Descendants()
            .Select(element => (string?)element.Attribute("Click"))
            .Where(value => value is not null)
            .ToHashSet(StringComparer.Ordinal);
        var code = File.ReadAllText(FixturePath("MainPage.xaml.cs"));

        Assert.Contains("Mirror_Click", clickHandlers);
        Assert.Contains("EnableTcpIp_Click", clickHandlers);
        Assert.Contains("Reboot_Click", clickHandlers);
        Assert.Contains("Disconnect_Click", clickHandlers);
        Assert.DoesNotContain("DeviceMore_Click", clickHandlers);
        Assert.DoesNotContain("DeviceMore_Click", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceCardAlignsActionsRightAndReflowsAtNarrowWidths()
    {
        var actionPanel = ElementNamed("ActionPanel");
        var adaptiveWidths = Markup.Descendants(Ui + "AdaptiveTrigger")
            .Select(trigger => (string?)trigger.Attribute("MinWindowWidth"))
            .Where(width => width is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal("2", (string?)actionPanel.Attribute("Grid.Column"));
        Assert.Equal("Right", (string?)actionPanel.Attribute("HorizontalAlignment"));
        Assert.Contains("900", adaptiveWidths);
        Assert.Contains("600", adaptiveWidths);
        Assert.Contains("0", adaptiveWidths);
        Assert.NotNull(ElementNamed("MirrorButton"));
        Assert.NotNull(ElementNamed("TcpIpButton"));
        Assert.NotNull(ElementNamed("RebootButton"));
        Assert.NotNull(ElementNamed("DisconnectButton"));
    }

    [Fact]
    public void ConnectUsesCurrentEditorTextAndRejectsBlankEndpoint()
    {
        Assert.Equal("{Binding Endpoint, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}",
            (string?)ElementNamed("EndpointBox").Attribute("Text"));

        var pageCode = File.ReadAllText(FixturePath("MainPage.xaml.cs"));
        var viewModelCode = File.ReadAllText(FixturePath("MainViewModel.cs"));

        Assert.Contains("ViewModel.Endpoint = EndpointBox.Text?.Trim() ?? string.Empty;", pageCode, StringComparison.Ordinal);
        Assert.Contains("if (string.IsNullOrWhiteSpace(endpoint))", viewModelCode, StringComparison.Ordinal);
        Assert.Contains("StatusText = \"请输入设备 IP 地址和端口\";", viewModelCode, StringComparison.Ordinal);
        Assert.Contains("Endpoint = string.Empty;", viewModelCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionMethodsAreSideBySideWithoutNestedPairingExpander()
    {
        var methods = ElementNamed("ConnectionMethodsGrid");
        var pairingCard = ElementNamed("PairingCard");
        var adaptiveWidths = methods.Descendants(Ui + "AdaptiveTrigger")
            .Select(trigger => (string?)trigger.Attribute("MinWindowWidth"))
            .ToArray();

        Assert.Equal("1", (string?)pairingCard.Attribute("Grid.Column"));
        Assert.Empty(methods.Descendants(Ui + "Expander"));
        Assert.Contains("480", adaptiveWidths);
        Assert.Contains("0", adaptiveWidths);
        Assert.Contains(
            methods.Descendants(Ui + "Button"),
            button => (string?)button.Attribute("Content") == "配对设备"
                      && (string?)button.Attribute("Click") == "Pair_Click");
    }

    [Fact]
    public void AppRegistersAndRedirectsToSingleMainInstance()
    {
        var code = File.ReadAllText(FixturePath("App.xaml.cs"));

        Assert.Contains("AppInstance.FindOrRegisterForKey(MainInstanceKey)", code, StringComparison.Ordinal);
        Assert.Contains("RedirectActivationToAsync", code, StringComparison.Ordinal);
        Assert.Contains("ActivateExistingWindow", code, StringComparison.Ordinal);
    }

    private static XDocument Markup => XDocument.Load(FixturePath("MainPage.xaml"));

    private static XElement ElementNamed(string name) => Markup.Descendants()
        .Single(element => (string?)element.Attribute(Xaml + "Name") == name);

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
}
