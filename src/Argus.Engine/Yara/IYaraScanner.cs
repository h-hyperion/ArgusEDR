using Argus.Core.Models;

namespace Argus.Engine.Yara;

public interface IYaraScanner
{
    Task<ThreatResult> ScanFileAsync(string filePath, CancellationToken ct = default);
    Task<ThreatResult> ScanBytesAsync(byte[] data, string label, CancellationToken ct = default);
}
