using Argus.Core.Models;
using dnYara;

namespace Argus.Engine.Yara;

public sealed class YaraScanner : IYaraScanner, IDisposable
{
    private readonly YaraContext _ctx;
    private readonly CompiledRules _rules;

    public YaraScanner(IEnumerable<string> ruleTexts)
    {
        _ctx = new YaraContext();
        using var compiler = new Compiler();
        foreach (var rule in ruleTexts)
            compiler.AddRuleString(rule);
        _rules = compiler.Compile();
    }

    public Task<ThreatResult> ScanFileAsync(string filePath, CancellationToken ct = default)
    {
        var bytes = File.ReadAllBytes(filePath);
        return ScanBytesAsync(bytes, filePath, ct);
    }

    public Task<ThreatResult> ScanBytesAsync(byte[] data, string label, CancellationToken ct = default)
    {
        try
        {
            var scanner = new dnYara.Scanner();
            var buffer = data;  // local copy for ref parameter
            var matches = scanner.ScanMemory(ref buffer, _rules);

            if (matches.Count == 0)
                return Task.FromResult(ThreatResult.Clean(label));

            var evidence = string.Join(", ", matches.Select(m => m.MatchingRule.Identifier));
            return Task.FromResult(ThreatResult.Malicious(label, $"YARA:{evidence}", 90));
        }
        catch (Exception ex)
        {
            // FAIL CLOSED: YARA errors must never return Clean
            Serilog.Log.Warning(ex, "YARA scan failed for {Label} — returning Unknown (fail closed)", label);
            return Task.FromResult(ThreatResult.Unknown(label, $"YARA error: {ex.Message}"));
        }
    }

    public void Dispose() => _rules.Dispose();
}
