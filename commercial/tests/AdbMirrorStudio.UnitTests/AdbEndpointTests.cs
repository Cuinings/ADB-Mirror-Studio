using AdbMirrorStudio.Infrastructure.Adb;

namespace AdbMirrorStudio.UnitTests;

public sealed class AdbEndpointTests
{
    [Theory]
    [InlineData("192.168.1.20", "192.168.1.20:5555")]
    [InlineData("192.168.1.20:37099", "192.168.1.20:37099")]
    [InlineData("android-lab.local", "android-lab.local:5555")]
    [InlineData("[2001:db8::1]:37111", "[2001:db8::1]:37111")]
    [InlineData("2001:db8::1", "[2001:db8::1]:5555")]
    public void Normalize_ReturnsCanonicalEndpoint(string input, string expected)
    {
        Assert.Equal(expected, AdbEndpoint.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("host:70000")]
    [InlineData("not a host")]
    public void Normalize_RejectsInvalidEndpoint(string input)
    {
        Assert.ThrowsAny<ArgumentException>(() => AdbEndpoint.Normalize(input));
    }
}

