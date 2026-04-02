using Argus.Core.Models;
using Argus.Scanner;
using FluentAssertions;
using Moq;

namespace Argus.Scanner.Tests;

public class DeepScannerTests
{
    [Fact]
    public async Task ScanFile_CleanFile_ReturnsClean()
    {
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "Hello, world!");

        var mockEngine = new Mock<IScanEngine>();
        mockEngine.Setup(e => e.ScanFileAsync(tempFile, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(ThreatResult.Clean(tempFile));

        var scanner = new DeepScanner(mockEngine.Object);
        var result = await scanner.ScanFileAsync(tempFile);

        result.IsClean.Should().BeTrue();
        File.Delete(tempFile);
    }

    [Fact]
    public async Task ScanFile_MaliciousFile_ReturnsMalicious()
    {
        var tempFile = Path.GetTempFileName();
        var mockEngine = new Mock<IScanEngine>();
        mockEngine.Setup(e => e.ScanFileAsync(tempFile, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(ThreatResult.Malicious(tempFile, "YARA:Trojan", 95));

        var scanner = new DeepScanner(mockEngine.Object);
        var result = await scanner.ScanFileAsync(tempFile);

        result.IsMalicious.Should().BeTrue();
        result.Evidence.Should().Contain("YARA:Trojan");
        File.Delete(tempFile);
    }

    [Fact]
    public async Task ScanDirectory_ScansAllExes_ReturnsResults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var file1 = Path.Combine(tempDir, "a.exe");
        var file2 = Path.Combine(tempDir, "b.exe");
        await File.WriteAllTextAsync(file1, "content1");
        await File.WriteAllTextAsync(file2, "content2");

        var mockEngine = new Mock<IScanEngine>();
        mockEngine.Setup(e => e.ScanFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((string f, CancellationToken _) => ThreatResult.Clean(f));

        var scanner = new DeepScanner(mockEngine.Object);
        var results = await scanner.ScanDirectoryAsync(tempDir, "*.exe");

        results.Should().HaveCount(2);
        results.Should().AllSatisfy(r => r.IsClean.Should().BeTrue());
        Directory.Delete(tempDir, true);
    }
}
