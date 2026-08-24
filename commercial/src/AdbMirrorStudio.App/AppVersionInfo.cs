namespace AdbMirrorStudio.App;

internal static class AppVersionInfo
{
    public static string ProductVersion { get; } =
        $"V{typeof(AppVersionInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";
}
