namespace AdbMirrorStudio.Domain.Settings;

public sealed record AppSettings(
    int SchemaVersion,
    string Theme,
    string Language,
    string LastEndpoint,
    bool AutoRefresh,
    string MirrorProfileId = "balanced",
    bool FirstRunCompleted = false,
    bool AutoReconnect = true,
    bool HasConnectedBefore = false)
{
    public static AppSettings Default { get; } = new(
        1,
        "System",
        "zh-CN",
        "192.168.1.100:5555",
        true);
}
