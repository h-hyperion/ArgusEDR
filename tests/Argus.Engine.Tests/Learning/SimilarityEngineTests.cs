using Argus.Engine.Learning;
using FluentAssertions;

namespace Argus.Engine.Tests.Learning;

public class SimilarityEngineTests
{
    [Fact]
    public void Score_IdenticalProfiles_Returns100()
    {
        var engine = new SimilarityEngine();
        var profile = new BehaviorProfile { ProcessName = "svchost", ImagePath = @"C:\Windows\System32\svchost.exe" };

        var score = engine.Score(profile, profile);

        score.Should().Be(100);
    }

    [Fact]
    public void Score_DifferentProcessNames_ReducesScore()
    {
        var engine = new SimilarityEngine();
        var known   = new BehaviorProfile { ProcessName = "svchost",   ImagePath = @"C:\Windows\System32\svchost.exe" };
        var unknown = new BehaviorProfile { ProcessName = "svch0st",   ImagePath = @"C:\Temp\svch0st.exe" };

        var score = engine.Score(unknown, known);

        score.Should().BeLessThan(80);
    }

    [Fact]
    public void Score_SameImagePathDifferentCase_Returns100()
    {
        var engine = new SimilarityEngine();
        var a = new BehaviorProfile { ProcessName = "notepad", ImagePath = @"C:\Windows\notepad.exe" };
        var b = new BehaviorProfile { ProcessName = "notepad", ImagePath = @"C:\WINDOWS\NOTEPAD.EXE" };

        engine.Score(a, b).Should().Be(100);
    }
}
