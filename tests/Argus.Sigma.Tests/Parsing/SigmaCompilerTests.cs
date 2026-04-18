using Argus.Sigma.Engine;
using Argus.Sigma.Matching;
using Argus.Sigma.Parsing;

namespace Argus.Sigma.Tests.Parsing;

public class SigmaCompilerTests
{
    private static CompiledRule CompileFromYaml(string yaml) =>
        SigmaCompiler.Compile(SigmaYamlParser.Parse(yaml));

    private static SigmaEventContext ProcEvent(string commandLine, string image = "C:\\Windows\\System32\\cmd.exe") =>
        new("process_creation", new Dictionary<string, string?>
        {
            ["CommandLine"] = commandLine,
            ["Image"] = image
        });

    [Fact]
    public void Contains_modifier_matches_substring()
    {
        var rule = CompileFromYaml("""
            title: whoami
            id: 00000000-0000-0000-0000-000000000001
            logsource: { category: process_creation }
            detection:
                sel: { CommandLine|contains: whoami }
                condition: sel
            """);

        rule.Evaluate(ProcEvent("cmd.exe /c whoami /all")).Should().BeTrue();
        rule.Evaluate(ProcEvent("cmd.exe /c dir")).Should().BeFalse();
    }

    [Fact]
    public void Endswith_modifier_with_value_list_is_OR()
    {
        var rule = CompileFromYaml("""
            title: shells
            id: 00000000-0000-0000-0000-000000000002
            logsource: { category: process_creation }
            detection:
                sel:
                    Image|endswith:
                        - \cmd.exe
                        - \powershell.exe
                condition: sel
            """);

        rule.Evaluate(ProcEvent("x", image: "C:\\Windows\\System32\\cmd.exe")).Should().BeTrue();
        rule.Evaluate(ProcEvent("x", image: "C:\\Windows\\System32\\powershell.exe")).Should().BeTrue();
        rule.Evaluate(ProcEvent("x", image: "C:\\Windows\\System32\\wscript.exe")).Should().BeFalse();
    }

    [Fact]
    public void Multiple_fields_in_selection_are_AND()
    {
        var rule = CompileFromYaml("""
            title: multi
            id: 00000000-0000-0000-0000-000000000003
            logsource: { category: process_creation }
            detection:
                sel:
                    Image|endswith: \powershell.exe
                    CommandLine|contains: -enc
                condition: sel
            """);

        rule.Evaluate(ProcEvent("powershell.exe -enc ABCD", image: "C:\\w\\powershell.exe")).Should().BeTrue();
        rule.Evaluate(ProcEvent("powershell.exe -command get", image: "C:\\w\\powershell.exe")).Should().BeFalse();
        rule.Evaluate(ProcEvent("cmd.exe -enc ABCD", image: "C:\\w\\cmd.exe")).Should().BeFalse();
    }

    [Fact]
    public void All_modifier_requires_every_value_to_match()
    {
        var rule = CompileFromYaml("""
            title: all-mod
            id: 00000000-0000-0000-0000-000000000004
            logsource: { category: process_creation }
            detection:
                sel:
                    CommandLine|contains|all:
                        - powershell
                        - -enc
                condition: sel
            """);

        rule.Evaluate(ProcEvent("powershell.exe -enc ABCD")).Should().BeTrue();
        rule.Evaluate(ProcEvent("powershell.exe -command get")).Should().BeFalse();
    }

    [Fact]
    public void Not_condition_inverts()
    {
        var rule = CompileFromYaml("""
            title: negation
            id: 00000000-0000-0000-0000-000000000005
            logsource: { category: process_creation }
            detection:
                suspicious: { CommandLine|contains: whoami }
                known_good: { Image|endswith: \trusted.exe }
                condition: suspicious and not known_good
            """);

        rule.Evaluate(ProcEvent("whoami", image: "C:\\w\\evil.exe")).Should().BeTrue();
        rule.Evaluate(ProcEvent("whoami", image: "C:\\w\\trusted.exe")).Should().BeFalse();
    }

    [Fact]
    public void Unknown_modifier_fails_closed()
    {
        Action act = () => CompileFromYaml("""
            title: bad
            id: 00000000-0000-0000-0000-000000000006
            logsource: { category: process_creation }
            detection:
                sel: { CommandLine|xyz: whoami }
                condition: sel
            """);

        act.Should().Throw<SigmaParseException>().WithMessage("*xyz*");
    }

    [Fact]
    public void Default_modifier_is_equals_case_insensitive()
    {
        var rule = CompileFromYaml("""
            title: eq
            id: 00000000-0000-0000-0000-000000000007
            logsource: { category: process_creation }
            detection:
                sel: { Image: C:\Windows\System32\cmd.exe }
                condition: sel
            """);

        rule.Evaluate(ProcEvent("x", image: "c:\\windows\\system32\\CMD.EXE")).Should().BeTrue();
        rule.Evaluate(ProcEvent("x", image: "c:\\other\\cmd.exe")).Should().BeFalse();
    }
}
