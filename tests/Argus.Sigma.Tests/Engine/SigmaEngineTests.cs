using Argus.Sigma.Engine;
using Argus.Sigma.Matching;
using Argus.Sigma.Parsing;

namespace Argus.Sigma.Tests.Engine;

public class SigmaEngineTests
{
    private const string WhoamiRuleYaml = """
        title: Suspicious whoami
        id: 10000000-0000-0000-0000-000000000001
        level: high
        logsource: { category: process_creation }
        detection:
            sel: { CommandLine|contains: whoami }
            condition: sel
        """;

    private const string RegistryRuleYaml = """
        title: Autorun persistence
        id: 10000000-0000-0000-0000-000000000002
        level: medium
        logsource: { category: registry_event }
        detection:
            sel: { TargetObject|contains: \Run\ }
            condition: sel
        """;

    private static SigmaEventContext ProcEvent(string cmdLine) =>
        new("process_creation", new Dictionary<string, string?> { ["CommandLine"] = cmdLine });

    [Fact]
    public void Engine_emits_match_for_rule_in_matching_category()
    {
        var engine = new SigmaEngine();
        engine.AddRule(SigmaCompiler.Compile(SigmaYamlParser.Parse(WhoamiRuleYaml)));

        var captured = new CapturingSink();
        engine.AddSink(captured);

        var matches = engine.Evaluate(ProcEvent("cmd.exe /c whoami"));

        matches.Should().HaveCount(1);
        matches[0].Rule.Title.Should().Be("Suspicious whoami");
        captured.Received.Should().HaveCount(1);
    }

    [Fact]
    public void Engine_skips_rules_in_other_categories()
    {
        var engine = new SigmaEngine();
        engine.AddRule(SigmaCompiler.Compile(SigmaYamlParser.Parse(RegistryRuleYaml)));

        var matches = engine.Evaluate(ProcEvent("cmd.exe /c whoami"));
        matches.Should().BeEmpty();
    }

    [Fact]
    public void Engine_evaluates_multiple_rules_independently()
    {
        var engine = new SigmaEngine();
        engine.AddRule(SigmaCompiler.Compile(SigmaYamlParser.Parse(WhoamiRuleYaml)));
        engine.AddRule(SigmaCompiler.Compile(SigmaYamlParser.Parse(RegistryRuleYaml)));

        engine.RuleCount.Should().Be(2);
        engine.Evaluate(ProcEvent("cmd.exe /c whoami")).Should().HaveCount(1);
    }

    [Fact]
    public void Engine_returns_empty_when_no_rules_match()
    {
        var engine = new SigmaEngine();
        engine.AddRule(SigmaCompiler.Compile(SigmaYamlParser.Parse(WhoamiRuleYaml)));

        engine.Evaluate(ProcEvent("cmd.exe /c dir")).Should().BeEmpty();
    }

    [Fact]
    public void Sink_exception_does_not_prevent_other_sinks_or_return()
    {
        var engine = new SigmaEngine();
        engine.AddRule(SigmaCompiler.Compile(SigmaYamlParser.Parse(WhoamiRuleYaml)));

        engine.AddSink(new ThrowingSink());
        var good = new CapturingSink();
        engine.AddSink(good);

        var matches = engine.Evaluate(ProcEvent("whoami"));
        matches.Should().HaveCount(1);
        good.Received.Should().HaveCount(1);
    }

    private sealed class CapturingSink : IDetectionSink
    {
        public List<SigmaMatch> Received { get; } = new();
        public void Emit(SigmaMatch m) => Received.Add(m);
    }

    private sealed class ThrowingSink : IDetectionSink
    {
        public void Emit(SigmaMatch m) => throw new InvalidOperationException("boom");
    }
}
