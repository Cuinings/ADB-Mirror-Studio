using System.Text.Json;
using System.Text.Json.Serialization;
using AdbMirrorStudio.Domain.Settings;
using AdbMirrorStudio.Infrastructure.Updates;

namespace AdbMirrorStudio.Infrastructure.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(GitHubRelease))]
internal partial class AdbMirrorStudioJsonContext : JsonSerializerContext;
