using Argus.Core.Models;
using Serilog;

namespace Argus.Scanner;

public sealed class DeepScanner
{
    private readonly IScanEngine _engine;

    public DeepScanner(IScanEngine engine) => _engine = engine;

    public async Task<ThreatResult> ScanFileAsync(string filePath, CancellationToken ct = default)
    {
        Log.Debug("Scanning file: {FilePath}", filePath);
        return await _engine.ScanFileAsync(filePath, ct);
    }

    public async Task<IReadOnlyList<ThreatResult>> ScanDirectoryAsync(
        string directoryPath, string pattern = "*.*", CancellationToken ct = default)
    {
        var files = Directory.GetFiles(directoryPath, pattern, SearchOption.AllDirectories);
        var results = new List<ThreatResult>(files.Length);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await _engine.ScanFileAsync(file, ct));
        }

        return results;
    }
}
