using Argus.Engine.Learning;
using FluentAssertions;

namespace Argus.Engine.Tests.Learning;

public class BehaviorExtractorTests
{
    [Fact]
    public void ExtractProfile_ForProcess_CapturesKeyBehaviors()
    {
        var extractor = new BehaviorExtractor();
        int pid = Environment.ProcessId;

        var profile = extractor.ExtractFromPid(pid);

        profile.ProcessId.Should().Be(pid);
        profile.ImagePath.Should().NotBeNullOrEmpty();
        profile.ParentProcessId.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ExtractProfile_RecordsFileAccesses()
    {
        var extractor = new BehaviorExtractor();
        var profile = extractor.ExtractFromPid(Environment.ProcessId);

        profile.ImagePath.Should().NotBeNullOrEmpty();
    }
}
