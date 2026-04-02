using System.Security.Cryptography;
using System.Text.Json;
using Argus.Core;

namespace Argus.Engine.Yara;

/// <summary>
/// Verifies YARA rule files against a DPAPI-protected SHA-256 manifest.
/// Detects tampering, deletion, or injection of unauthorized rules.
/// </summary>
public static class YaraRuleVerifier
{
    /// <summary>
    /// Generate manifest from current rule files (called during install/update).
    /// </summary>
    public static void GenerateManifest(string rulesDirectory)
    {
        var hashes = new Dictionary<string, string>();
        foreach (var file in Directory.GetFiles(rulesDirectory, "*.yar"))
        {
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
            hashes[Path.GetFileName(file)] = hash;
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(hashes);
        var protectedManifest = ProtectedData.Protect(json, null, DataProtectionScope.LocalMachine);
        File.WriteAllBytes(ArgusConstants.YaraManifestPath, protectedManifest);
    }

    /// <summary>
    /// Verify all rule files match the manifest. Returns list of violations.
    /// Called by YaraScanner before loading rules.
    /// </summary>
    public static List<string> Verify(string rulesDirectory)
    {
        var violations = new List<string>();

        if (!File.Exists(ArgusConstants.YaraManifestPath))
        {
            violations.Add("YARA rule manifest not found — rules may be unverified");
            return violations;
        }

        var protectedManifest = File.ReadAllBytes(ArgusConstants.YaraManifestPath);
        var json = ProtectedData.Unprotect(protectedManifest, null, DataProtectionScope.LocalMachine);
        var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        var currentFiles = Directory.GetFiles(rulesDirectory, "*.yar")
            .Select(Path.GetFileName).ToHashSet();

        // Check for tampered or missing rules
        foreach (var (fileName, expectedHash) in expected)
        {
            var filePath = Path.Combine(rulesDirectory, fileName);
            if (!File.Exists(filePath))
            {
                violations.Add($"YARA rule MISSING: {fileName}");
                continue;
            }
            var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(filePath)));
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                violations.Add($"YARA rule TAMPERED: {fileName}");
        }

        // Check for injected (unexpected) rules
        foreach (var file in currentFiles)
        {
            if (!expected.ContainsKey(file!))
                violations.Add($"YARA rule INJECTED (not in manifest): {file}");
        }

        return violations;
    }
}
