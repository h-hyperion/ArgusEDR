using System.Security.Cryptography;
using System.Text.Json;
using Argus.Core.Models;
using Serilog;

namespace Argus.Defender.Quarantine;

public sealed class QuarantineStore : IQuarantineStore
{
    private readonly string _dir;
    private readonly List<QuarantineEntry> _index = new();

    public QuarantineStore(string quarantineDirectory)
    {
        _dir = quarantineDirectory;
        Directory.CreateDirectory(_dir);
        LoadIndex();
    }

    public async Task QuarantineAsync(string filePath, ThreatResult threat, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var encPath = Path.Combine(_dir, $"{id}.enc");

        var plaintext = await File.ReadAllBytesAsync(filePath, ct);
        var originalSize = plaintext.Length;
        var hash = Convert.ToHexString(SHA256.HashData(plaintext));

        // AES-256-GCM encryption
        using var aes = new AesGcm(GetOrCreateKey(), AesGcm.TagByteSizes.MaxSize);
        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];

        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Write: [12-byte nonce][16-byte tag][ciphertext]
        await using var fs = File.Create(encPath);
        await fs.WriteAsync(nonce, ct);
        await fs.WriteAsync(tag, ct);
        await fs.WriteAsync(ciphertext, ct);

        // Remove original
        File.Delete(filePath);

        _index.Add(new QuarantineEntry
        {
            Id = id,
            OriginalPath = filePath,
            EncryptedPath = encPath,
            ThreatLevel = threat.Level,
            Evidence = threat.Evidence ?? "Unknown",
            QuarantinedAt = DateTimeOffset.UtcNow,
            OriginalSize = originalSize,
            OriginalHash = hash
        });

        await SaveIndexAsync(ct);
        Log.Information("Quarantined {FilePath} as {Id} ({ThreatLevel})", filePath, id, threat.Level);
    }

    public async Task<string> RestoreAsync(string entryId, string restorePath, CancellationToken ct = default)
    {
        var entry = _index.FirstOrDefault(e => e.Id == entryId)
            ?? throw new KeyNotFoundException($"Quarantine entry {entryId} not found");

        var encData = await File.ReadAllBytesAsync(entry.EncryptedPath, ct);

        var nonce = encData[..AesGcm.NonceByteSizes.MaxSize];
        var tag = encData[AesGcm.NonceByteSizes.MaxSize..(AesGcm.NonceByteSizes.MaxSize + AesGcm.TagByteSizes.MaxSize)];
        var ciphertext = encData[(AesGcm.NonceByteSizes.MaxSize + AesGcm.TagByteSizes.MaxSize)..];

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(GetOrCreateKey(), AesGcm.TagByteSizes.MaxSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        var targetPath = Path.Combine(restorePath, Path.GetFileName(entry.OriginalPath));
        await File.WriteAllBytesAsync(targetPath, plaintext, ct);

        Log.Information("Restored quarantine entry {Id} to {Path}", entryId, targetPath);
        return targetPath;
    }

    public IReadOnlyList<QuarantineEntry> List() => _index.AsReadOnly();

    private byte[] GetOrCreateKey()
    {
        var keyPath = Path.Combine(_dir, ".qkey");
        if (File.Exists(keyPath))
        {
            var protectedBytes = File.ReadAllBytes(keyPath);
            return ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.LocalMachine);
        }

        var key = new byte[32]; // 256-bit AES key
        RandomNumberGenerator.Fill(key);
        var protectedKey = ProtectedData.Protect(key, null, DataProtectionScope.LocalMachine);
        File.WriteAllBytes(keyPath, protectedKey);
        return key;
    }

    private void LoadIndex()
    {
        var indexPath = Path.Combine(_dir, "index.json");
        if (!File.Exists(indexPath)) return;
        var entries = JsonSerializer.Deserialize<List<QuarantineEntry>>(
            File.ReadAllText(indexPath));
        if (entries is not null) _index.AddRange(entries);
    }

    private async Task SaveIndexAsync(CancellationToken ct) =>
        await File.WriteAllTextAsync(Path.Combine(_dir, "index.json"),
            JsonSerializer.Serialize(_index), ct);
}
