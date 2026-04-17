namespace Argus.Watchdog.Supervision;

using System.Security.Cryptography;
using System.Text.Json;

/// <summary>
/// Verifies binary SHA-256 hashes against a manifest.json file.
/// </summary>
public sealed class ManifestVerifier
{
    /// <summary>
    /// Verifies the SHA-256 hash of <paramref name="filePath"/> against the
    /// entry in <paramref name="manifestPath"/>.
    /// </summary>
    /// <returns>true if hash matches, false if hash mismatch.</returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown when manifest file doesn't exist, or manifest has no entry for the file.
    /// </exception>
    public bool Verify(string filePath, string manifestPath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Binary not found", filePath);

        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Manifest not found", manifestPath);

        var json = File.ReadAllText(manifestPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var manifest = JsonSerializer.Deserialize<ManifestFile>(json, options)
            ?? throw new InvalidOperationException("Invalid manifest");

        var fileName = Path.GetFileName(filePath);

        var foundEntry = manifest.Files
            .FirstOrDefault(kvp => string.Equals(kvp.Key, fileName, StringComparison.OrdinalIgnoreCase));

        if (foundEntry.Key is null)
            throw new FileNotFoundException($"No manifest entry for '{fileName}'", fileName);

        var expectedHash = foundEntry.Value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? foundEntry.Value[7..]
            : foundEntry.Value;

        using var stream = File.OpenRead(filePath);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream));

        return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class ManifestFile
{
    public string Version { get; set; } = "";
    public Dictionary<string, string> Files { get; set; } = new();
}
