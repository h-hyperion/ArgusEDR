using Argus.Recovery;
using FluentAssertions;

namespace Argus.Recovery.Tests;

public class BackupManagerTests
{
    [Fact]
    public async Task CreateBackup_ThenVerify_ReturnsValid()
    {
        var backupDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var manager = new BackupManager(backupDir);

        var sourceFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(sourceFile, "Argus module binary content");

        await manager.CreateBackupAsync("Argus.Scanner", sourceFile);
        var isValid = await manager.VerifyBackupAsync("Argus.Scanner");

        isValid.Should().BeTrue();
        Directory.Delete(backupDir, true);
        File.Delete(sourceFile);
    }

    [Fact]
    public async Task VerifyBackup_TamperedFile_ReturnsFalse()
    {
        var backupDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var manager = new BackupManager(backupDir);

        var sourceFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(sourceFile, "Original content");
        await manager.CreateBackupAsync("Argus.Test", sourceFile);

        // Tamper with the backup
        var backupFile = Directory.GetFiles(backupDir, "Argus.Test*.enc").First();
        await File.WriteAllTextAsync(backupFile, "Tampered!");

        var isValid = await manager.VerifyBackupAsync("Argus.Test");
        isValid.Should().BeFalse();

        Directory.Delete(backupDir, true);
        File.Delete(sourceFile);
    }
}
