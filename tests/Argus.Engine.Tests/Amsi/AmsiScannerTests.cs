using Argus.Core.Models;
using Argus.Engine.Amsi;
using FluentAssertions;
using Moq;

namespace Argus.Engine.Tests.Amsi;

public class AmsiScannerTests
{
    [Fact]
    public async Task ScanBuffer_CleanContent_ReturnsClean()
    {
        var mockAmsi = new Mock<IAmsiNative>();
        mockAmsi.Setup(a => a.ScanBuffer(It.IsAny<byte[]>(), It.IsAny<string>()))
                .Returns(AmsiResult.Clean);

        var scanner = new AmsiScanner(mockAmsi.Object);
        var result = await scanner.ScanBufferAsync("Hello, world!"u8.ToArray(), "test.ps1");

        result.IsClean.Should().BeTrue();
    }

    [Fact]
    public async Task ScanBuffer_MaliciousContent_ReturnsMalicious()
    {
        var mockAmsi = new Mock<IAmsiNative>();
        mockAmsi.Setup(a => a.ScanBuffer(It.IsAny<byte[]>(), It.IsAny<string>()))
                .Returns(AmsiResult.Detected);

        var scanner = new AmsiScanner(mockAmsi.Object);
        var result = await scanner.ScanBufferAsync("X5O!P%@AP"u8.ToArray(), "eicar.ps1");

        result.IsMalicious.Should().BeTrue();
        result.Evidence.Should().Contain("AMSI");
    }

    [Fact]
    public async Task ScanBuffer_AmsiThrows_ReturnsUnknown_FailClosed()
    {
        var mockAmsi = new Mock<IAmsiNative>();
        mockAmsi.Setup(a => a.ScanBuffer(It.IsAny<byte[]>(), It.IsAny<string>()))
                .Throws<InvalidOperationException>();

        var scanner = new AmsiScanner(mockAmsi.Object);
        var result = await scanner.ScanBufferAsync(new byte[] { 0x00 }, "unknown");

        // SECURITY: AMSI errors must return Unknown (fail closed), never Clean
        result.IsUnknown.Should().BeTrue();
        result.Evidence.Should().Contain("ScanError");
    }
}
