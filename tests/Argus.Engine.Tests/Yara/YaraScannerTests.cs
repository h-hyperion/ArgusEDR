using Argus.Core.Models;
using Argus.Engine.Yara;
using FluentAssertions;

namespace Argus.Engine.Tests.Yara;

public class YaraScannerTests
{
    [Fact]
    public async Task ScanBytes_WithMatchingRule_ReturnsMalicious()
    {
        const string rule = "rule TestMatch { strings: $s = \"EICAR\" condition: $s }";
        var scanner = new YaraScanner(new[] { rule });
        var bytes = "This is an EICAR test string"u8.ToArray();

        var result = await scanner.ScanBytesAsync(bytes, "test.bin");

        result.IsMalicious.Should().BeTrue();
        result.Evidence.Should().Contain("TestMatch");
    }

    [Fact]
    public async Task ScanBytes_WithNoMatch_ReturnsClean()
    {
        const string rule = "rule TestMatch { strings: $s = \"EICAR\" condition: $s }";
        var scanner = new YaraScanner(new[] { rule });
        var bytes = "Hello, world!"u8.ToArray();

        var result = await scanner.ScanBytesAsync(bytes, "clean.txt");

        result.IsClean.Should().BeTrue();
    }
}
