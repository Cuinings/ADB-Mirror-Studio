using AdbMirrorStudio.Application.Mirroring;
using AdbMirrorStudio.Domain.Mirroring;

namespace AdbMirrorStudio.UnitTests;

public sealed class ScrcpyArgumentBuilderTests
{
    [Fact]
    public void Build_BalancedProfile_ProducesSafeIndividualArguments()
    {
        var arguments = ScrcpyArgumentBuilder.Build("192.168.1.8:37111", MirrorProfile.Balanced, "测试设备");

        Assert.Contains("--serial=192.168.1.8:37111", arguments);
        Assert.Contains("--window-title=测试设备", arguments);
        Assert.Contains("--max-size=1920", arguments);
        Assert.Contains("--max-fps=60", arguments);
        Assert.Contains("--video-bit-rate=8M", arguments);
        Assert.Contains("--stay-awake", arguments);
        Assert.DoesNotContain("--no-audio", arguments);
    }

    [Fact]
    public void Build_RejectsUnsafeRanges()
    {
        var profile = MirrorProfile.Balanced with { MaxFps = 1000 };
        Assert.Throws<ArgumentOutOfRangeException>(() => ScrcpyArgumentBuilder.Build("device", profile));
    }

    [Fact]
    public void Build_RecordingProfile_AddsRecordingAndPresentationOptions()
    {
        var path = Path.Combine(Path.GetTempPath(), "镜像 录制.mkv");
        var profile = MirrorProfile.Presentation with { RecordPath = path };

        var arguments = ScrcpyArgumentBuilder.Build("device", profile);

        Assert.Contains($"--record={Path.GetFullPath(path)}", arguments);
        Assert.Contains("--fullscreen", arguments);
        Assert.Contains("--always-on-top", arguments);
        Assert.Contains("--turn-screen-off", arguments);
        Assert.Contains("--no-control", arguments);
    }

    [Fact]
    public void Build_RejectsUnsupportedRecordingContainer()
    {
        var profile = MirrorProfile.Balanced with { RecordPath = "recording.avi" };
        Assert.Throws<ArgumentException>(() => ScrcpyArgumentBuilder.Build("device", profile));
    }
}
