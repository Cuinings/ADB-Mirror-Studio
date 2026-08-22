namespace AdbMirrorStudio.Domain.Devices;

public enum DeviceState
{
    Unknown,
    Discovered,
    Pairing,
    Connecting,
    Online,
    Offline,
    Unauthorized,
    Recovery,
    Bootloader,
    Error
}

public enum ConnectionKind
{
    Unknown,
    Usb,
    TcpIp
}

public sealed record DeviceInfo(
    string Serial,
    string Model,
    string Product,
    DeviceState State,
    ConnectionKind ConnectionKind,
    DateTimeOffset LastSeen)
{
    public string DisplayName => Model == "—" ? Serial : Model;
}

public sealed record MdnsService(string Name, string ServiceType, string Endpoint);

public sealed record DeviceSnapshot(long Version, DateTimeOffset CapturedAt, IReadOnlyList<DeviceInfo> Devices);

