using Argus.Sigma.Matching;

namespace Argus.Sigma.Tests.Matching;

public class SigmaEventContextTests
{
    [Fact]
    public void GetField_returns_value_when_present()
    {
        var ctx = new SigmaEventContext(
            category: "process_creation",
            fields: new Dictionary<string, string?>
            {
                ["CommandLine"] = "powershell.exe -enc ABCD"
            });

        ctx.GetField("CommandLine").Should().Be("powershell.exe -enc ABCD");
    }

    [Fact]
    public void GetField_is_case_insensitive_for_name()
    {
        var ctx = new SigmaEventContext(
            category: "process_creation",
            fields: new Dictionary<string, string?> { ["CommandLine"] = "x" });

        ctx.GetField("commandline").Should().Be("x");
    }

    [Fact]
    public void GetField_returns_null_when_missing()
    {
        var ctx = new SigmaEventContext("process_creation", new Dictionary<string, string?>());
        ctx.GetField("Nothing").Should().BeNull();
    }
}
