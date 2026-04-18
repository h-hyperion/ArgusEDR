using Argus.Sigma.Matching;

namespace Argus.Sigma.Engine;

public interface ISigmaEngine
{
    int RuleCount { get; }
    void AddRule(CompiledRule rule);
    void AddSink(IDetectionSink sink);
    IReadOnlyList<SigmaMatch> Evaluate(SigmaEventContext evt);
}
