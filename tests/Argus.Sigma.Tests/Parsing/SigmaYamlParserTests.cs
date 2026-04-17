using Argus.Sigma.Models;
using Argus.Sigma.Parsing;

namespace Argus.Sigma.Tests.Parsing;

public class SigmaYamlParserTests
{
    private const string MinimalRule = """
        title: Test rule
        id: 11111111-2222-3333-4444-555555555555
        status: experimental
        description: A demo rule
        author: Argus
        level: high
        logsource:
            category: process_creation
            product: windows
        detection:
            selection:
                CommandLine|contains: whoami
            condition: selection
        tags:
            - attack.discovery
            - attack.t1033
        falsepositives:
            - System administrators
        references:
            - https://example.com/whoami
        """;

    [Fact]
    public void Parse_extracts_header_fields()
    {
        var rule = SigmaYamlParser.Parse(MinimalRule);
        rule.Id.Should().Be("11111111-2222-3333-4444-555555555555");
        rule.Title.Should().Be("Test rule");
        rule.Description.Should().Be("A demo rule");
        rule.Author.Should().Be("Argus");
        rule.Level.Should().Be(SigmaLevel.High);
    }

    [Fact]
    public void Parse_extracts_logsource()
    {
        var rule = SigmaYamlParser.Parse(MinimalRule);
        rule.LogSource.Category.Should().Be("process_creation");
        rule.LogSource.Product.Should().Be("windows");
        rule.LogSource.Service.Should().BeNull();
    }

    [Fact]
    public void Parse_extracts_selection_and_condition()
    {
        var rule = SigmaYamlParser.Parse(MinimalRule);
        rule.Detection.Condition.Should().Be("selection");
        rule.Detection.Selections.Should().ContainKey("selection");
        var sel = rule.Detection.Selections["selection"];
        var spec = sel.Fields.Keys.Single();
        spec.FieldName.Should().Be("CommandLine");
        spec.Modifiers.Should().BeEquivalentTo(new[] { "contains" });
        sel.Fields[spec].Should().BeEquivalentTo(new[] { "whoami" });
    }

    [Fact]
    public void Parse_handles_value_list()
    {
        const string yaml = """
            title: t
            id: 00000000-0000-0000-0000-000000000000
            logsource: { category: process_creation }
            detection:
                sel:
                    Image|endswith:
                        - \cmd.exe
                        - \powershell.exe
                condition: sel
            """;
        var rule = SigmaYamlParser.Parse(yaml);
        var values = rule.Detection.Selections["sel"].Fields.Values.Single();
        values.Should().BeEquivalentTo(new[] { "\\cmd.exe", "\\powershell.exe" });
    }

    [Fact]
    public void Parse_defaults_level_when_missing()
    {
        const string yaml = """
            title: t
            id: 00000000-0000-0000-0000-000000000000
            logsource: { category: process_creation }
            detection:
                sel: { CommandLine: foo }
                condition: sel
            """;
        SigmaYamlParser.Parse(yaml).Level.Should().Be(SigmaLevel.Medium);
    }

    [Fact]
    public void Parse_rejects_missing_title()
    {
        const string yaml = """
            id: 00000000-0000-0000-0000-000000000000
            logsource: { category: process_creation }
            detection: { sel: { x: y }, condition: sel }
            """;
        Action act = () => SigmaYamlParser.Parse(yaml);
        act.Should().Throw<SigmaParseException>().WithMessage("*title*");
    }

    [Fact]
    public void Parse_rejects_missing_condition()
    {
        const string yaml = """
            title: t
            id: 00000000-0000-0000-0000-000000000000
            logsource: { category: process_creation }
            detection: { sel: { x: y } }
            """;
        Action act = () => SigmaYamlParser.Parse(yaml);
        act.Should().Throw<SigmaParseException>().WithMessage("*condition*");
    }
}
