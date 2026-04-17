using Argus.Sigma.Matching;

namespace Argus.Sigma.Engine;

public sealed class SigmaEngine : ISigmaEngine
{
    private readonly List<CompiledRule> _rules = new();
    private readonly List<IDetectionSink> _sinks = new();

    public int RuleCount => _rules.Count;

    public void AddRule(CompiledRule rule) => _rules.Add(rule);
    public void AddSink(IDetectionSink sink) => _sinks.Add(sink);

    public IReadOnlyList<SigmaMatch> Evaluate(SigmaEventContext evt)
    {
        var results = new List<SigmaMatch>();
        foreach (var rule in _rules)
        {
            if (!CategoryMatches(rule, evt)) continue;
            if (!rule.Evaluate(evt)) continue;
            var match = new SigmaMatch(rule.Source, evt, DateTimeOffset.UtcNow);
            results.Add(match);
            foreach (var sink in _sinks)
            {
                try { sink.Emit(match); }
                catch { /* sink isolation: one bad sink must not break detection */ }
            }
        }
        return results;
    }

    private static bool CategoryMatches(CompiledRule rule, SigmaEventContext evt) =>
        rule.Source.LogSource.Category is null ||
        string.Equals(rule.Source.LogSource.Category, evt.Category, StringComparison.OrdinalIgnoreCase);
}
