namespace Argus.Watchdog.Tests.Supervision;

using System.Security.Cryptography;
using System.Text.Json;
using Argus.Watchdog.Supervision;

internal sealed class ManifestFile
{
    public string Version { get; set; } = "";
    public Dictionary<string, string> Files { get; set; } = new();
}

public class ManifestVerifierTests : IDisposable
{
    private readonly string _testDir;
    private readonly ManifestVerifier _verifier;

    public ManifestVerifierTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"manifest_verifier_tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _verifier = new ManifestVerifier();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public void Verify_ReturnsTrue_WhenHashMatches()
    {
        // Arrange
        var content = "Hello, World!"u8.ToArray();
        var filePath = Path.Combine(_testDir, "test.exe");
        File.WriteAllBytes(filePath, content);

        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLower();
        var manifestFile = new ManifestFile
        {
            Version = "2.1.1",
            Files = new Dictionary<string, string>
            {
                { "test.exe", $"sha256:{hash}" }
            }
        };
        var manifestPath = Path.Combine(_testDir, "manifest.json");
        var json = JsonSerializer.Serialize(manifestFile);
        File.WriteAllText(manifestPath, json);

        // Act
        var result = _verifier.Verify(filePath, manifestPath);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenFileHasBeenTampered()
    {
        // Arrange
        var originalContent = "Hello, World!"u8.ToArray();
        var filePath = Path.Combine(_testDir, "test.exe");
        File.WriteAllBytes(filePath, originalContent);

        var hash = Convert.ToHexString(SHA256.HashData(originalContent)).ToLower();
        var manifestFile = new ManifestFile
        {
            Version = "2.1.1",
            Files = new Dictionary<string, string>
            {
                { "test.exe", $"sha256:{hash}" }
            }
        };
        var manifestPath = Path.Combine(_testDir, "manifest.json");
        var json = JsonSerializer.Serialize(manifestFile);
        File.WriteAllText(manifestPath, json);

        // Tamper with file
        var tamperedContent = new List<byte>(originalContent) { 0xFF }.ToArray();
        File.WriteAllBytes(filePath, tamperedContent);

        // Act
        var result = _verifier.Verify(filePath, manifestPath);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_ThrowsFileNotFoundException_WhenManifestHasNoEntryForFile()
    {
        // Arrange
        var content = "Hello, World!"u8.ToArray();
        var filePath = Path.Combine(_testDir, "test.exe");
        File.WriteAllBytes(filePath, content);

        var manifestFile = new ManifestFile
        {
            Version = "2.1.1",
            Files = new Dictionary<string, string>()
        };
        var manifestPath = Path.Combine(_testDir, "manifest.json");
        var json = JsonSerializer.Serialize(manifestFile);
        File.WriteAllText(manifestPath, json);

        // Act & Assert
        var ex = Assert.Throws<FileNotFoundException>(() => _verifier.Verify(filePath, manifestPath));
        ex.Message.Should().Contain("No manifest entry");
    }

    [Fact]
    public void Verify_ThrowsFileNotFoundException_WhenFileDoeNotExist()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "nonexistent.exe");
        var manifestFile = new ManifestFile
        {
            Version = "2.1.1",
            Files = new Dictionary<string, string>
            {
                { "nonexistent.exe", "sha256:abc123" }
            }
        };
        var manifestPath = Path.Combine(_testDir, "manifest.json");
        var json = JsonSerializer.Serialize(manifestFile);
        File.WriteAllText(manifestPath, json);

        // Act & Assert
        var ex = Assert.Throws<FileNotFoundException>(() => _verifier.Verify(filePath, manifestPath));
        ex.Message.Should().Contain("Binary not found");
    }

    [Fact]
    public void Verify_PerformsCaseInsensitiveFilenameMatching()
    {
        // Arrange
        var content = "Hello, World!"u8.ToArray();
        var filePath = Path.Combine(_testDir, "Test.EXE");
        File.WriteAllBytes(filePath, content);

        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLower();
        var manifestFile = new ManifestFile
        {
            Version = "2.1.1",
            Files = new Dictionary<string, string>
            {
                { "test.exe", $"sha256:{hash}" }
            }
        };
        var manifestPath = Path.Combine(_testDir, "manifest.json");
        var json = JsonSerializer.Serialize(manifestFile);
        File.WriteAllText(manifestPath, json);

        // Act
        var result = _verifier.Verify(filePath, manifestPath);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_ThrowsFileNotFoundException_WhenManifestFileDoesNotExist()
    {
        // Arrange
        var content = "Hello, World!"u8.ToArray();
        var filePath = Path.Combine(_testDir, "test.exe");
        File.WriteAllBytes(filePath, content);

        var manifestPath = Path.Combine(_testDir, "nonexistent_manifest.json");

        // Act & Assert
        var ex = Assert.Throws<FileNotFoundException>(() => _verifier.Verify(filePath, manifestPath));
        ex.Message.Should().Contain("Manifest not found");
    }

    [Fact]
    public void Verify_AcceptsHashWithoutSha256Prefix()
    {
        // Arrange
        var content = "Hello, World!"u8.ToArray();
        var filePath = Path.Combine(_testDir, "test.exe");
        File.WriteAllBytes(filePath, content);

        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLower();
        var manifestFile = new ManifestFile
        {
            Version = "2.1.1",
            Files = new Dictionary<string, string>
            {
                { "test.exe", hash }  // No "sha256:" prefix
            }
        };
        var manifestPath = Path.Combine(_testDir, "manifest.json");
        var json = JsonSerializer.Serialize(manifestFile);
        File.WriteAllText(manifestPath, json);

        // Act
        var result = _verifier.Verify(filePath, manifestPath);

        // Assert
        result.Should().BeTrue();
    }
}
