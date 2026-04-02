using Argus.Core.Models;

namespace Argus.Scanner;

public interface IScanEngine
{
    Task<ThreatResult> ScanFileAsync(string filePath, CancellationToken ct = default);
}
