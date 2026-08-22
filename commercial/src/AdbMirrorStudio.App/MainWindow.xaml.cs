using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace AdbMirrorStudio.App;

public sealed partial class MainWindow : Window
{
    public MainWindow(AppServices services)
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Title = "ADB Mirror Studio";
        SizeAndCenterWindow();
        RootFrame.Navigate(typeof(MainPage), services);
    }

    private void SizeAndCenterWindow()
    {
        const double desiredWidthDip = 1180;
        const double desiredHeightDip = 780;

        var windowHandle = WindowNative.GetWindowHandle(this);
        var scale = Math.Max(1.0, NativeMethods.GetDpiForWindow(windowHandle) / 96.0);
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        var desiredWidth = (int)Math.Round(desiredWidthDip * scale);
        var desiredHeight = (int)Math.Round(desiredHeightDip * scale);
        var width = Math.Min(desiredWidth, (int)Math.Round(workArea.Width * 0.92));
        var height = Math.Min(desiredHeight, (int)Math.Round(workArea.Height * 0.88));
        var x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
        var y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);

        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(nint windowHandle);
    }
}
