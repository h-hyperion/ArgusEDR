using Argus.Core.Models;

namespace Argus.Engine.Amsi;

public sealed class AmsiScanner : IAmsiScanner
{
    private readonly IAmsiNative _native;

    public AmsiScanner(IAmsiNative native) => _native = native;

    public Task<ThreatResult> ScanBufferAsync(byte[] buffer, string contentName,
        CancellationToken ct = default)
    {
        try
        {
            var result = _native.ScanBuffer(buffer, contentName);
            return Task.FromResult(result == AmsiResult.Detected
                ? ThreatResult.Malicious(contentName, "AMSI:Detected", 95)
                : ThreatResult.Clean(contentName));
        }
        catch (Exception ex)
        {
            // FAIL CLOSED: Never return Clean on scanner error.
            // An AMSI crash could be caused by malware tampering with the AMSI provider.
            Serilog.Log.Warning(ex, "AMSI scan failed for {Content} — returning Unknown (fail closed)", contentName);
            return Task.FromResult(ThreatResult.Unknown(contentName, $"AMSI error: {ex.Message}"));
        }
    }

    public async Task<ThreatResult> ScanFileAsync(string filePath,
        CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, ct);
        return await ScanBufferAsync(bytes, Path.GetFileName(filePath), ct);
    }
}
