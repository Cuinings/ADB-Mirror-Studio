namespace AdbMirrorStudio.Domain.Settings;

public static class ConnectionHistory
{
    public const int MaximumEntries = 10;

    public static string[] Add(IEnumerable<string>? existing, string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        return Normalize(new[] { endpoint }.Concat(existing ?? []));
    }

    public static string[] Normalize(IEnumerable<string>? endpoints) =>
        (endpoints ?? [])
        .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint))
        .Select(endpoint => endpoint.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(MaximumEntries)
        .ToArray();
}
