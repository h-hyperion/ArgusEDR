using Argus.Core.Models;
using Argus.Defender.Quarantine;
using FluentAssertions;

namespace Argus.Defender.Tests.Quarantine;

public class QuarantineStoreTests
{
    private static string TempDir() =>
        Directory.CreateTempSubdirectory("argus_qtest_").FullName;

    [Fact]
    public async Task Quarantine_StoresFileEncrypted_OriginalNoLongerExists()
    {
        var dir = TempDir();
        var store = new QuarantineStore(dir);
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "malware payload");

        await store.QuarantineAsync(tempFile,
            ThreatResult.Malicious(tempFile, "YARA:Ransomware", 95));

        File.Exists(tempFile).Should().BeFalse("original must be removed after quarantine");
        store.List().Should().HaveCount(1);

        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Quarantine_CanRestore_RestoredContentMatchesOriginal()
    {
        var dir = TempDir();
        var store = new QuarantineStore(dir);
        var tempFile = Path.GetTempFileName();
        var originalContent = "this is test malware content";
        await File.WriteAllTextAsync(tempFile, originalContent);

        await store.QuarantineAsync(tempFile,
            ThreatResult.Malicious(tempFile, "YARA:Test", 90));

        var entry = store.List().Single();
        var restoreDir = TempDir();
        var restoredPath = await store.RestoreAsync(entry.Id, restoreDir);

        var restoredContent = await File.ReadAllTextAsync(restoredPath);
        restoredContent.Should().Be(originalContent);

        Directory.Delete(dir, true);
        Directory.Delete(restoreDir, true);
    }

    [Fact]
    public async Task Quarantine_EncryptedFile_IsNotPlaintext()
    {
        var dir = TempDir();
        var store = new QuarantineStore(dir);
        var tempFile = Path.GetTempFileName();
        var content = "sensitive malware payload data";
        await File.WriteAllTextAsync(tempFile, content);

        await store.QuarantineAsync(tempFile,
            ThreatResult.Malicious(tempFile, "YARA:Test", 90));

        var entry = store.List().Single();
        var encryptedBytes = await File.ReadAllBytesAsync(entry.EncryptedPath);
        var encryptedText = System.Text.Encoding.UTF8.GetString(encryptedBytes);
        encryptedText.Should().NotContain(content, "encrypted file must not contain plaintext");

        Directory.Delete(dir, true);
    }
}
