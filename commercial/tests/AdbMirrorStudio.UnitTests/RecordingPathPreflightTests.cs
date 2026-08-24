using AdbMirrorStudio.Infrastructure.Scrcpy;

namespace AdbMirrorStudio.UnitTests;

public sealed class RecordingPathPreflightTests
{
    [Fact]
    public void PrepareRecordPath_NormalizesAndVerifiesWritableFile()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "录屏 文件.MKV");

            var result = MirrorSessionManager.PrepareRecordPath(path);

            Assert.Equal(Path.GetFullPath(path), result);
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PrepareRecordPath_RejectsMissingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "recording.mp4");

        Assert.Throws<DirectoryNotFoundException>(() => MirrorSessionManager.PrepareRecordPath(path));
    }

    [Fact]
    public void PrepareRecordPath_RejectsLockedFile()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "recording.mp4");
            using var lockedFile = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

            Assert.Throws<IOException>(() => MirrorSessionManager.PrepareRecordPath(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"adb-mirror-studio-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
