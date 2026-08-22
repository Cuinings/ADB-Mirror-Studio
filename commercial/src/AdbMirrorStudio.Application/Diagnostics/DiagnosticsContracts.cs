namespace AdbMirrorStudio.Application.Diagnostics;

public enum DiagnosticSeverity
{
    Success,
    Warning,
    Error
}

public sealed record DiagnosticItem(
    string Id,
    string Title,
    string Detail,
    DiagnosticSeverity Severity);

public interface IDiagnosticsService
{
    Task<IReadOnlyList<DiagnosticItem>> RunAsync(CancellationToken cancellationToken = default);
}

