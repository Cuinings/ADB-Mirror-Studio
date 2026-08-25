using AdbMirrorStudio.Domain.Mirroring;

namespace AdbMirrorStudio.Application.Mirroring;

public static class MirrorRecordingProfile
{
    public static MirrorProfile Create(MirrorSession session, string? recordPath)
    {
        ArgumentNullException.ThrowIfNull(session);
        var profile = session.Profile ?? (MirrorProfile.Balanced with
        {
            Name = session.ProfileName,
            MaxSize = session.MaxSize,
            MaxFps = session.MaxFps,
            VideoBitRateMbps = session.VideoBitRateMbps,
            VideoCodec = session.VideoCodec
        });
        var normalizedPath = string.IsNullOrWhiteSpace(recordPath) ? null : Path.GetFullPath(recordPath.Trim());
        return profile with
        {
            RecordPath = normalizedPath,
            Fullscreen = normalizedPath is null && profile.Fullscreen,
            AlwaysOnTop = normalizedPath is null && profile.AlwaysOnTop
        };
    }
}
