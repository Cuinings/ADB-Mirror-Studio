using System.Net;

namespace AdbMirrorStudio.Infrastructure.Adb;

public static class AdbEndpoint
{
    public static string Normalize(string value, int defaultPort = 5555)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var input = value.Trim();

        if (Uri.TryCreate($"tcp://{input}", UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            var port = uri.IsDefaultPort ? defaultPort : uri.Port;
            ValidatePort(port);
            var host = uri.Host.Trim('[', ']');
            return uri.HostNameType == UriHostNameType.IPv6
                ? $"[{host}]:{port}"
                : $"{host}:{port}";
        }

        // A bare IPv6 address is ambiguous to Uri; accept it and add brackets.
        if (IPAddress.TryParse(input, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return $"[{address}]:{defaultPort}";
        }

        throw new ArgumentException("请输入有效的 IPv4、IPv6 或主机名地址。", nameof(value));
    }

    private static void ValidatePort(int port)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port), "端口必须介于 1 和 65535 之间。");
    }
}
