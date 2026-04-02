using Argus.Core.Models;

namespace Argus.Engine.Amsi;

public interface IAmsiScanner
{
    Task<ThreatResult> ScanBufferAsync(byte[] buffer, string contentName,
        CancellationToken ct = default);
    Task<ThreatResult> ScanFileAsync(string filePath, CancellationToken ct = default);
}
