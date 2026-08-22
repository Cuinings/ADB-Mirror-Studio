using AdbMirrorStudio.Infrastructure.Processes;

namespace AdbMirrorStudio.UnitTests;

public sealed class BoundedTextTailReaderTests
{
    [Fact]
    public async Task ReadAsync_ReturnsAllTextWithinLimit()
    {
        var result = await BoundedTextTailReader.ReadAsync(new StringReader("scrcpy ready"), 64);

        Assert.Equal("scrcpy ready", result);
    }

    [Fact]
    public async Task ReadAsync_KeepsOnlyNewestTextBeyondLimit()
    {
        var input = string.Concat(Enumerable.Range(0, 100).Select(value => (char)('A' + value % 26)));

        var result = await BoundedTextTailReader.ReadAsync(new StringReader(input), 32);

        Assert.Equal(32, result.Length);
        Assert.Equal(input[^32..], result);
    }
}
