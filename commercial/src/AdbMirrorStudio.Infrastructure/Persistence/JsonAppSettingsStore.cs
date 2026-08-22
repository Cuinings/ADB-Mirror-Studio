using System.Text.Json;
using AdbMirrorStudio.Application.Settings;
using AdbMirrorStudio.Domain.Settings;
using AdbMirrorStudio.Infrastructure.Serialization;

namespace AdbMirrorStudio.Infrastructure.Persistence;

public sealed class JsonAppSettingsStore(string filePath) : IAppSettingsStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(filePath)) return AppSettings.Default;
            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync(
                    stream,
                    AdbMirrorStudioJsonContext.Default.AppSettings,
                    cancellationToken)
                .ConfigureAwait(false) ?? AppSettings.Default;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return AppSettings.Default;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporaryPath = filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        settings,
                        AdbMirrorStudioJsonContext.Default.AppSettings,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Preserve the primary save result when a scanner briefly locks the temporary file.
            }
            _gate.Release();
        }
    }
}
