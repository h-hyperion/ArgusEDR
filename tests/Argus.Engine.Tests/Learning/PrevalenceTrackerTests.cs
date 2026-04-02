using Argus.Engine.Learning;
using FluentAssertions;

namespace Argus.Engine.Tests.Learning;

public class PrevalenceTrackerTests
{
    [Fact]
    public void RecordSighting_IncreasesCount()
    {
        using var db = new SignatureDatabase(":memory:");
        var tracker = new PrevalenceTracker(db);

        tracker.RecordSighting("explorer.exe", @"C:\Windows\explorer.exe");
        tracker.RecordSighting("explorer.exe", @"C:\Windows\explorer.exe");

        tracker.GetSightingCount("explorer.exe").Should().Be(2);
    }

    [Fact]
    public void IsRare_NewProcess_ReturnsTrue()
    {
        using var db = new SignatureDatabase(":memory:");
        var tracker = new PrevalenceTracker(db);

        tracker.IsRare("brandnewprocess.exe").Should().BeTrue();
    }

    [Fact]
    public void IsRare_FrequentProcess_ReturnsFalse()
    {
        using var db = new SignatureDatabase(":memory:");
        var tracker = new PrevalenceTracker(db);

        for (int i = 0; i < 20; i++)
            tracker.RecordSighting("svchost.exe", @"C:\Windows\System32\svchost.exe");

        tracker.IsRare("svchost.exe").Should().BeFalse();
    }
}
