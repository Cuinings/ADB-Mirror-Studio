using AdbMirrorStudio.Domain.Mirroring;

namespace AdbMirrorStudio.Application.Mirroring;

public static class ScrcpyArgumentBuilder
{
    public static IReadOnlyList<string> Build(string serial, MirrorProfile profile, string? windowTitle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        Validate(profile);

        var arguments = new List<string>
        {
            $"--serial={serial}",
            $"--window-title={windowTitle ?? serial}"
        };

        if (profile.MaxSize > 0) arguments.Add($"--max-size={profile.MaxSize}");
        if (profile.MaxFps > 0) arguments.Add($"--max-fps={profile.MaxFps}");
        if (profile.VideoBitRateMbps > 0) arguments.Add($"--video-bit-rate={profile.VideoBitRateMbps}M");
        if (!profile.AudioEnabled) arguments.Add("--no-audio");
        if (profile.StayAwake) arguments.Add("--stay-awake");
        if (profile.TurnScreenOff) arguments.Add("--turn-screen-off");
        if (profile.Fullscreen) arguments.Add("--fullscreen");
        if (profile.AlwaysOnTop) arguments.Add("--always-on-top");
        if (profile.ReadOnly) arguments.Add("--no-control");
        if (!string.IsNullOrWhiteSpace(profile.RecordPath)) arguments.Add($"--record={Path.GetFullPath(profile.RecordPath)}");

        return arguments;
    }

    private static void Validate(MirrorProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.MaxSize is < 0 or > 8192) throw new ArgumentOutOfRangeException(nameof(profile.MaxSize));
        if (profile.MaxFps is < 0 or > 240) throw new ArgumentOutOfRangeException(nameof(profile.MaxFps));
        if (profile.VideoBitRateMbps is < 0 or > 200) throw new ArgumentOutOfRangeException(nameof(profile.VideoBitRateMbps));
        if (!string.IsNullOrWhiteSpace(profile.RecordPath))
        {
            var extension = Path.GetExtension(profile.RecordPath);
            if (extension is not (".mp4" or ".mkv"))
            {
                throw new ArgumentException("录屏文件必须使用 .mp4 或 .mkv 扩展名。", nameof(profile.RecordPath));
            }
        }
    }
}
