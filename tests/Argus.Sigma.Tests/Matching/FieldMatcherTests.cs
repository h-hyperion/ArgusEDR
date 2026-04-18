using Argus.Sigma.Matching;

namespace Argus.Sigma.Tests.Matching;

public class FieldMatcherTests
{
    [Fact]
    public void Equals_matches_exact_ignoring_case()
    {
        var m = FieldMatchers.Equals("powershell.exe");
        m.Match("POWERSHELL.EXE").Should().BeTrue();
        m.Match("cmd.exe").Should().BeFalse();
    }

    [Fact]
    public void Equals_returns_false_for_null()
    {
        FieldMatchers.Equals("x").Match(null).Should().BeFalse();
    }

    [Fact]
    public void Contains_matches_substring()
    {
        var m = FieldMatchers.Contains("whoami");
        m.Match("cmd.exe /c whoami /all").Should().BeTrue();
        m.Match("cmd.exe /c dir").Should().BeFalse();
    }

    [Fact]
    public void StartsWith_matches_prefix()
    {
        var m = FieldMatchers.StartsWith("C:\\Windows");
        m.Match("c:\\windows\\system32\\cmd.exe").Should().BeTrue();
        m.Match("D:\\other").Should().BeFalse();
    }

    [Fact]
    public void EndsWith_matches_suffix()
    {
        var m = FieldMatchers.EndsWith(".exe");
        m.Match("c:\\foo\\bar.EXE").Should().BeTrue();
        m.Match("c:\\foo\\bar.dll").Should().BeFalse();
    }

    [Fact]
    public void Regex_matches_pattern()
    {
        var m = FieldMatchers.Regex("^powershell\\.exe\\s+-[eE]");
        m.Match("powershell.exe -EncodedCommand abc").Should().BeTrue();
        m.Match("powershell.exe -Command get").Should().BeFalse();
    }

    [Fact]
    public void AnyOf_matches_if_any_inner_matches()
    {
        var m = FieldMatchers.AnyOf(new[]
        {
            FieldMatchers.Equals("cmd.exe"),
            FieldMatchers.Equals("powershell.exe")
        });
        m.Match("CMD.EXE").Should().BeTrue();
        m.Match("wscript.exe").Should().BeFalse();
    }

    [Fact]
    public void All_matches_only_if_every_inner_matches()
    {
        var m = FieldMatchers.All(new[]
        {
            FieldMatchers.Contains("powershell"),
            FieldMatchers.Contains("-enc")
        });
        m.Match("powershell.exe -enc ABCD").Should().BeTrue();
        m.Match("powershell.exe -command get").Should().BeFalse();
    }
}
