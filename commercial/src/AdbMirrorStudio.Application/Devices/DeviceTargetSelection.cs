using AdbMirrorStudio.Domain.Devices;

namespace AdbMirrorStudio.Application.Devices;

public static class DeviceTargetSelection
{
    public static string? Resolve(string? currentSerial, IEnumerable<DeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        var onlineDevices = devices.Where(device => device.State == DeviceState.Online).ToArray();
        if (!string.IsNullOrWhiteSpace(currentSerial)
            && onlineDevices.Any(device => string.Equals(device.Serial, currentSerial, StringComparison.Ordinal)))
        {
            return currentSerial;
        }

        return onlineDevices.FirstOrDefault()?.Serial;
    }
}
