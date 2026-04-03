using System.Security.Cryptography;
using System.Text.Json;

namespace Argus.Recovery;

/// <summary>
/// Plants hidden canary files and detects if any were modified or deleted.
/// Watchdog calls VerifyAsync() every 60 seconds.
/// </summary>
public sealed class CanaryManager
{
    private readonly string _watchDir;
    private readonly string _manifestPath;
    private const int CanaryCount = 5;

    public CanaryManager(string watchDirectory)
    {
        _watchDir     = watchDirectory;
        _manifestPath = Path.Combine(watchDirectory, ".canary_manifest");
    }

    public async Task PlantAsync()
    {
        var manifest = new Dictionary<string, string>();

        for (int i = 0; i < CanaryCount; i++)
        {
            var name    = Guid.NewGuid().ToString("N") + ".canary";
            var path    = Path.Combine(_watchDir, name);
            var content = RandomNumberGenerator.GetBytes(128);

            await File.WriteAllBytesAsync(path, content);
            File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System);

            manifest[name] = Convert.ToHexString(SHA256.HashData(content));
        }

        var manifestJson = JsonSerializer.Serialize(manifest);
        var hmac = ComputeHmac(manifestJson);
        var envelope = JsonSerializer.Serialize(new { Data = manifestJson, Hmac = hmac });
        await File.WriteAllTextAsync(_manifestPath, envelope);
        File.SetAttributes(_manifestPath, FileAttributes.Hidden | FileAttributes.System);
    }

    public async Task<bool> VerifyAsync()
    {
        if (!File.Exists(_manifestPath)) return false;

        var envelope = JsonDocument.Parse(await File.ReadAllTextAsync(_manifestPath));
        var data = envelope.RootElement.GetProperty("Data").GetString()!;
        var storedHmac = envelope.RootElement.GetProperty("Hmac").GetString()!;

        if (!ComputeHmac(data).Equals(storedHmac, StringComparison.Ordinal))
            return false;

        var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(data)!;

        foreach (var (name, expectedHash) in manifest)
        {
            var path = Path.Combine(_watchDir, name);
            if (!File.Exists(path)) return false;

            var actualHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string ComputeHmac(string data)
    {
        var keyMaterial = System.Text.Encoding.UTF8.GetBytes(
            Environment.MachineName + "ArgusCanaryKey2025");
        using var hmac = new HMACSHA256(SHA256.HashData(keyMaterial));
        return Convert.ToHexString(
            hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data)));
    }
}
