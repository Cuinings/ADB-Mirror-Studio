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
    public void Build_RecordingProfile_UsesSafeWindowedPresentationOptions()
    {
        var path = Path.Combine(Path.GetTempPath(), "镜像 录制.mkv");
        var profile = MirrorProfile.Presentation with { RecordPath = path };

        var arguments = ScrcpyArgumentBuilder.Build("device", profile);

        Assert.Contains($"--record={Path.GetFullPath(path)}", arguments);
        Assert.DoesNotContain("--fullscreen", arguments);
        Assert.DoesNotContain("--always-on-top", arguments);
        Assert.Contains("--stay-awake", arguments);
        Assert.DoesNotContain("--turn-screen-off", arguments);
        Assert.DoesNotContain("--no-control", arguments);
    }

    [Fact]
    public void Presets_NeverForceFullscreenOrAlwaysOnTop()
    {
        foreach (var profile in MirrorProfile.Presets)
        {
            Assert.False(profile.Fullscreen);
            Assert.False(profile.AlwaysOnTop);
        }
    }

    [Fact]
    public void Build_ReadOnlyProfile_DropsOptionsThatRequireControl()
    {
        var profile = MirrorProfile.Balanced with { ReadOnly = true, TurnScreenOff = true };

        var arguments = ScrcpyArgumentBuilder.Build("device", profile);

        Assert.Contains("--no-control", arguments);
        Assert.DoesNotContain("--stay-awake", arguments);
        Assert.DoesNotContain("--turn-screen-off", arguments);
    }

    [Fact]
    public void Build_RejectsUnsupportedRecordingContainer()
    {
        var profile = MirrorProfile.Balanced with { RecordPath = "recording.avi" };
        Assert.Throws<ArgumentException>(() => ScrcpyArgumentBuilder.Build("device", profile));
    }

    [Fact]
    public void Build_AcceptsUppercaseRecordingContainer()
    {
        var profile = MirrorProfile.Balanced with { RecordPath = "recording.MP4" };

        var arguments = ScrcpyArgumentBuilder.Build("device", profile);

        Assert.Contains($"--record={Path.GetFullPath("recording.MP4")}", arguments);
    }

    [Fact]
    public void Build_AddsResolvedVideoCodec()
    {
        var profile = MirrorProfile.Quality with { VideoCodec = "h265" };

        var arguments = ScrcpyArgumentBuilder.Build("device", profile);

        Assert.Contains("--video-codec=h265", arguments);
    }

    [Fact]
    public void Build_RejectsUnknownVideoCodec()
    {
        var profile = MirrorProfile.Balanced with { VideoCodec = "unknown" };
        Assert.Throws<ArgumentOutOfRangeException>(() => ScrcpyArgumentBuilder.Build("device", profile));
    }
}
