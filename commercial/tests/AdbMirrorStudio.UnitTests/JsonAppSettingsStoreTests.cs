using AdbMirrorStudio.Domain.Settings;
using AdbMirrorStudio.Infrastructure.Persistence;

namespace AdbMirrorStudio.UnitTests;

public sealed class JsonAppSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AdbMirrorStudio.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoad_RoundTripsSettings()
    {
        var store = new JsonAppSettingsStore(Path.Combine(_directory, "settings.json"));
        var expected = AppSettings.Default with { Theme = "Dark", LastEndpoint = "pixel.local:5555" };

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        Assert.Equal(expected, actual);
        Assert.False(File.Exists(Path.Combine(_directory, "settings.json.tmp")));
    }

    [Fact]
    public async Task Load_CorruptFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, "{not-json");

        var actual = await new JsonAppSettingsStore(path).LoadAsync();

        Assert.Equal(AppSettings.Default, actual);
    }

    [Fact]
    public async Task Load_LockedFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, "{}");
        await using var lockStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var actual = await new JsonAppSettingsStore(path).LoadAsync();

        Assert.Equal(AppSettings.Default, actual);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
