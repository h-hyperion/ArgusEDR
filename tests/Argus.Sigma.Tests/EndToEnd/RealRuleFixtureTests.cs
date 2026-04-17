using Argus.Sigma.Engine;
using Argus.Sigma.Matching;
using Argus.Sigma.Parsing;

namespace Argus.Sigma.Tests.EndToEnd;

public class RealRuleFixtureTests
{
    private static readonly string ResourceDir = Path.Combine(AppContext.BaseDirectory, "EndToEnd", "Resources");

    private static CompiledRule Load(string fileName) =>
        SigmaCompiler.Compile(SigmaYamlParser.Parse(File.ReadAllText(Path.Combine(ResourceDir, fileName)), fileName));

    [Fact]
    public void All_fixture_rules_load_without_error()
    {
        var files = Directory.GetFiles(ResourceDir, "*.yml");
        files.Should().NotBeEmpty();

        foreach (var f in files)
        {
            Action act = () => SigmaCompiler.Compile(SigmaYamlParser.Parse(File.ReadAllText(f), f));
            act.Should().NotThrow($"rule {Path.GetFileName(f)} must load cleanly");
        }
    }

    [Fact]
    public void Whoami_rule_detects_whoami_process()
    {
        var rule = Load("proc_creation_whoami.yml");
        var evt = new SigmaEventContext("process_creation", new Dictionary<string, string?>
        {
            ["Image"] = "C:\\Windows\\System32\\whoami.exe"
        });
        rule.Evaluate(evt).Should().BeTrue();
    }

    [Fact]
    public void Whoami_rule_ignores_unrelated_process()
    {
        var rule = Load("proc_creation_whoami.yml");
        var evt = new SigmaEventContext("process_creation", new Dictionary<string, string?>
        {
            ["Image"] = "C:\\Windows\\System32\\notepad.exe"
        });
        rule.Evaluate(evt).Should().BeFalse();
    }

    [Fact]
    public void Powershell_encoded_rule_detects_encoded_flag()
    {
        var rule = Load("proc_creation_powershell_encoded.yml");
        var evt = new SigmaEventContext("process_creation", new Dictionary<string, string?>
        {
            ["Image"] = "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe",
            ["CommandLine"] = "powershell.exe -enc JABzAD0A"
        });
        rule.Evaluate(evt).Should().BeTrue();
    }

    [Fact]
    public void Powershell_encoded_rule_ignores_plain_commands()
    {
        var rule = Load("proc_creation_powershell_encoded.yml");
        var evt = new SigmaEventContext("process_creation", new Dictionary<string, string?>
        {
            ["Image"] = "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe",
            ["CommandLine"] = "powershell.exe -command Get-Process"
        });
        rule.Evaluate(evt).Should().BeFalse();
    }

    [Fact]
    public void Registry_autoruns_rule_detects_run_key_write()
    {
        var rule = Load("registry_autoruns.yml");
        var evt = new SigmaEventContext("registry_event", new Dictionary<string, string?>
        {
            ["TargetObject"] = "HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\Evil"
        });
        rule.Evaluate(evt).Should().BeTrue();
    }

    [Fact]
    public void Mimikatz_imageload_rule_detects_mimilib()
    {
        var rule = Load("image_load_mimikatz.yml");
        var evt = new SigmaEventContext("image_load", new Dictionary<string, string?>
        {
            ["ImageLoaded"] = "C:\\Attacker\\mimilib.dll"
        });
        rule.Evaluate(evt).Should().BeTrue();
    }

    [Fact]
    public void Engine_routes_event_to_only_matching_category()
    {
        var engine = new SigmaEngine();
        foreach (var f in Directory.GetFiles(ResourceDir, "*.yml"))
        {
            engine.AddRule(SigmaCompiler.Compile(SigmaYamlParser.Parse(File.ReadAllText(f), f)));
        }

        var registryEvent = new SigmaEventContext("registry_event", new Dictionary<string, string?>
        {
            ["TargetObject"] = "HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\X"
        });

        var matches = engine.Evaluate(registryEvent);
        matches.Should().HaveCount(1);
        matches[0].Rule.LogSource.Category.Should().Be("registry_event");
    }
}
