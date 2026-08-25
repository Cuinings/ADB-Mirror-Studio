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

    private static XDocument Markup => XDocument.Load(FixturePath("MainPage.xaml"));

    private static XElement ElementNamed(string name) => Markup.Descendants()
        .Single(element => (string?)element.Attribute(Xaml + "Name") == name);

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
}
