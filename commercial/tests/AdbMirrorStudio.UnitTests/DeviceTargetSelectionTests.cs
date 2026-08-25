using AdbMirrorStudio.Application.Devices;
using AdbMirrorStudio.Domain.Devices;

namespace AdbMirrorStudio.UnitTests;

public sealed class DeviceTargetSelectionTests
{
    [Fact]
    public void KeepsCurrentDeviceWhenItIsStillOnline()
    {
        var devices = new[]
        {
            Device("first", DeviceState.Online),
            Device("current", DeviceState.Online)
        };

        Assert.Equal("current", DeviceTargetSelection.Resolve("current", devices));
    }

    [Fact]
    public void FallsBackToFirstOnlineDeviceWhenCurrentDeviceDisconnects()
    {
        var devices = new[]
        {
            Device("offline", DeviceState.Offline),
            Device("first-online", DeviceState.Online),
            Device("second-online", DeviceState.Online)
        };

        Assert.Equal("first-online", DeviceTargetSelection.Resolve("missing", devices));
    }

    [Fact]
    public void DoesNotSelectUnauthorizedOrOfflineDevice()
    {
        var devices = new[]
        {
            Device("unauthorized", DeviceState.Unauthorized),
            Device("offline", DeviceState.Offline)
        };

        Assert.Null(DeviceTargetSelection.Resolve(null, devices));
    }

    [Fact]
    public void ReturnsNullForEmptySnapshot()
    {
        Assert.Null(DeviceTargetSelection.Resolve("old", Array.Empty<DeviceInfo>()));
    }

    private static DeviceInfo Device(string serial, DeviceState state) =>
        new(serial, serial, serial, state, ConnectionKind.Usb, DateTimeOffset.UtcNow);
}
