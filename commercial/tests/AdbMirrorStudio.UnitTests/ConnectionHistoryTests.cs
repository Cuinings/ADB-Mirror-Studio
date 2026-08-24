using AdbMirrorStudio.Domain.Settings;

namespace AdbMirrorStudio.UnitTests;

public sealed class ConnectionHistoryTests
{
    [Fact]
    public void Add_PromotesLatestAndRemovesDuplicates()
    {
        var history = ConnectionHistory.Add(
            ["pixel.local:5555", "192.168.1.8:5555"],
            " PIXEL.local:5555 ");

        Assert.Equal(["PIXEL.local:5555", "192.168.1.8:5555"], history);
    }

    [Fact]
    public void Add_KeepsOnlyTenMostRecentEndpoints()
    {
        var existing = Enumerable.Range(1, 10).Select(index => $"192.168.1.{index}:5555");

        var history = ConnectionHistory.Add(existing, "new-device.local:5555");

        Assert.Equal(ConnectionHistory.MaximumEntries, history.Length);
        Assert.Equal("new-device.local:5555", history[0]);
        Assert.DoesNotContain("192.168.1.10:5555", history);
    }

    [Fact]
    public void UpgradeConnectionHistory_DisablesLegacyAutomaticReconnect()
    {
        var legacy = new AppSettings(
            1,
            "System",
            "zh-CN",
            "pixel.local:5555",
            true,
            AutoReconnect: true,
            HasConnectedBefore: true);

        var upgraded = legacy.UpgradeConnectionHistory();

        Assert.Equal(AppSettings.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.False(upgraded.AutoReconnect);
        Assert.True(upgraded.HasConnectedBefore);
        Assert.Equal(["pixel.local:5555"], upgraded.RememberedEndpoints);
    }

    [Fact]
    public void UpgradeConnectionHistory_DoesNotRememberUnusedPlaceholder()
    {
        var legacy = new AppSettings(1, "System", "zh-CN", "192.168.1.100:5555", true);

        var upgraded = legacy.UpgradeConnectionHistory();

        Assert.Empty(upgraded.RememberedEndpoints);
        Assert.Empty(upgraded.LastEndpoint);
        Assert.False(upgraded.HasConnectedBefore);
    }
}
