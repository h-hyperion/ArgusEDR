using System.Security.Cryptography;
using System.Text.Json;

namespace Argus.Recovery;

public sealed class BackupManager
{
    private readonly string _backupDir;

    public BackupManager(string backupDir)
    {
        _backupDir = backupDir;
        Directory.CreateDirectory(backupDir);
    }

    public async Task CreateBackupAsync(string moduleName, string sourcePath)
    {
        var bytes = await File.ReadAllBytesAsync(sourcePath);
        var hash = SHA256.HashData(bytes);

        var key = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[bytes.Length];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, bytes, cipher, tag);

        var protectedKey = ProtectedData.Protect(key, null, DataProtectionScope.LocalMachine);

        var manifest = new BackupManifest
        {
            ModuleName = moduleName,
            SourceHash = Convert.ToHexString(hash),
            CreatedAt = DateTimeOffset.UtcNow,
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Key = Convert.ToBase64String(protectedKey)
        };

        var ts = manifest.CreatedAt.ToUnixTimeSeconds();
        await File.WriteAllBytesAsync(Path.Combine(_backupDir, $"{moduleName}_{ts}.enc"), cipher);
        await File.WriteAllTextAsync(Path.Combine(_backupDir, $"{moduleName}_{ts}.manifest"),
            JsonSerializer.Serialize(manifest));
    }

    public async Task<bool> VerifyBackupAsync(string moduleName)
    {
        var manifestFile = Directory
            .GetFiles(_backupDir, $"{moduleName}_*.manifest")
            .OrderByDescending(f => f)
            .FirstOrDefault();
        if (manifestFile is null) return false;

        var manifest = JsonSerializer.Deserialize<BackupManifest>(
            await File.ReadAllTextAsync(manifestFile));
        if (manifest is null) return false;

        var encFile = manifestFile.Replace(".manifest", ".enc");
        if (!File.Exists(encFile)) return false;

        var cipher = await File.ReadAllBytesAsync(encFile);
        var plain = new byte[cipher.Length];
        try
        {
            var protectedKey = Convert.FromBase64String(manifest.Key);
            var key = ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.LocalMachine);
            var nonce = Convert.FromBase64String(manifest.Nonce);
            var tag = Convert.FromBase64String(manifest.Tag);
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipher, tag, plain);
        }
        catch { return false; }

        var actualHash = Convert.ToHexString(SHA256.HashData(plain));
        return actualHash.Equals(manifest.SourceHash, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<byte[]?> RestoreAsync(string moduleName)
    {
        if (!await VerifyBackupAsync(moduleName)) return null;

        var manifestFile = Directory
            .GetFiles(_backupDir, $"{moduleName}_*.manifest")
            .OrderByDescending(f => f).First();
        var manifest = JsonSerializer.Deserialize<BackupManifest>(
            await File.ReadAllTextAsync(manifestFile))!;

        var cipher = await File.ReadAllBytesAsync(manifestFile.Replace(".manifest", ".enc"));
        var plain = new byte[cipher.Length];
        var protectedKey = Convert.FromBase64String(manifest.Key);
        var key = ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.LocalMachine);
        var nonce = Convert.FromBase64String(manifest.Nonce);
        var tag = Convert.FromBase64String(manifest.Tag);
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}

internal sealed record BackupManifest(
    string ModuleName, string SourceHash, DateTimeOffset CreatedAt,
    string Nonce, string Tag, string Key)
{
    public BackupManifest() : this("", "", default, "", "", "") { }
}
