using Argus.Sigma.Parsing;

namespace Argus.Sigma.Tests.Parsing;

public class SigmaFailClosedTests
{
    [Fact]
    public void Malformed_yaml_throws_with_clear_message()
    {
        const string broken = "title: [\n  unclosed";
        Action act = () => SigmaYamlParser.Parse(broken);
        act.Should().Throw<SigmaParseException>();
    }

    [Fact]
    public void Missing_logsource_throws()
    {
        const string yaml = """
            title: t
            id: 00000000-0000-0000-0000-000000000000
            detection: { sel: { x: y }, condition: sel }
            """;
        Action act = () => SigmaYamlParser.Parse(yaml);
        act.Should().Throw<SigmaParseException>().WithMessage("*logsource*");
    }

    [Fact]
    public void Missing_detection_throws()
    {
        const string yaml = """
            title: t
            id: 00000000-0000-0000-0000-000000000000
            logsource: { category: process_creation }
            """;
        Action act = () => SigmaYamlParser.Parse(yaml);
        act.Should().Throw<SigmaParseException>().WithMessage("*detection*");
    }

    [Fact]
    public void Unknown_modifier_throws_at_compile_time_not_evaluate_time()
    {
        const string yaml = """
            title: t
            id: 00000000-0000-0000-0000-000000000000
            logsource: { category: process_creation }
            detection:
                sel: { CommandLine|wat: foo }
                condition: sel
            """;
        var parsed = SigmaYamlParser.Parse(yaml);
        Action act = () => SigmaCompiler.Compile(parsed);
        act.Should().Throw<SigmaParseException>().WithMessage("*wat*");
    }

    [Fact]
    public void Condition_referencing_missing_selection_throws()
    {
        const string yaml = """
            title: t
            id: 00000000-0000-0000-0000-000000000000
            logsource: { category: process_creation }
            detection:
                real: { CommandLine: foo }
                condition: missing
            """;
        var parsed = SigmaYamlParser.Parse(yaml);
        Action act = () => SigmaCompiler.Compile(parsed);
        act.Should().Throw<FormatException>().WithMessage("*missing*");
    }

    [Fact]
    public void Invalid_regex_throws_with_pattern_in_message()
    {
        const string yaml = """
            title: t
            id: 00000000-0000-0000-0000-000000000000
            logsource: { category: process_creation }
            detection:
                sel: { CommandLine|re: '([unclosed' }
                condition: sel
            """;
        var parsed = SigmaYamlParser.Parse(yaml);
        Action act = () => SigmaCompiler.Compile(parsed);
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Empty_detection_throws()
    {
        const string yaml = """
            title: t
            id: 00000000-0000-0000-0000-000000000000
            logsource: { category: process_creation }
            detection: { condition: whatever }
            """;
        Action act = () => SigmaYamlParser.Parse(yaml);
        act.Should().Throw<SigmaParseException>();
    }
}
