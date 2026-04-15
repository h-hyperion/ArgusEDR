using System.Text.Json;
using Argus.Core.IPC;
using FluentAssertions;

namespace Argus.Core.Tests.IPC;

public class DefenderProtocolTests
{
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    // ── MonitorState ────────────────────────────────────────────────────────

    [Fact]
    public void MonitorState_RoundTripJson_PreservesAllFields()
    {
        var original = new MonitorState("etw-process", "ETW Process Monitor", true, "Running", 42_000L);

        var json = JsonSerializer.Serialize(original, _opts);
        var roundTripped = JsonSerializer.Deserialize<MonitorState>(json, _opts);

        roundTripped.Should().Be(original);
        roundTripped!.Id.Should().Be("etw-process");
        roundTripped.DisplayName.Should().Be("ETW Process Monitor");
        roundTripped.Enabled.Should().BeTrue();
        roundTripped.Status.Should().Be("Running");
        roundTripped.EventsEmitted.Should().Be(42_000L);
    }

    [Theory]
    [InlineData("Running")]
    [InlineData("Stopped")]
    [InlineData("NotImplemented")]
    [InlineData("Error")]
    public void MonitorState_AllStatusValues_RoundTripCorrectly(string status)
    {
        var original = new MonitorState("monitor-1", "Test Monitor", false, status, 0L);

        var json = JsonSerializer.Serialize(original, _opts);
        var roundTripped = JsonSerializer.Deserialize<MonitorState>(json, _opts);

        roundTripped!.Status.Should().Be(status);
    }

    // ── GetMonitorStatesRequest ─────────────────────────────────────────────

    [Fact]
    public void GetMonitorStatesRequest_RoundTripJson_ReturnsEqualInstance()
    {
        var original = new GetMonitorStatesRequest();

        var json = JsonSerializer.Serialize(original, _opts);
        var roundTripped = JsonSerializer.Deserialize<GetMonitorStatesRequest>(json, _opts);

        roundTripped.Should().Be(original);
    }

    // ── MonitorStatesResponse ───────────────────────────────────────────────

    [Fact]
    public void MonitorStatesResponse_RoundTripJson_PreservesMonitorList()
    {
        var monitors = new List<MonitorState>
        {
            new("etw-process",  "ETW Process Monitor",  true,  "Running",        100L),
            new("etw-registry", "ETW Registry Monitor", false, "Stopped",        0L),
            new("dns",          "DNS Protection",       true,  "NotImplemented", 0L),
        };
        var original = new MonitorStatesResponse(monitors);

        var json = JsonSerializer.Serialize(original, _opts);
        var roundTripped = JsonSerializer.Deserialize<MonitorStatesResponse>(json, _opts);

        roundTripped.Should().NotBeNull();
        roundTripped!.Monitors.Should().HaveCount(3);
        roundTripped.Monitors[0].Should().Be(monitors[0]);
        roundTripped.Monitors[1].Should().Be(monitors[1]);
        roundTripped.Monitors[2].Should().Be(monitors[2]);
    }

    [Fact]
    public void MonitorStatesResponse_EmptyList_RoundTripsCorrectly()
    {
        var original = new MonitorStatesResponse(Array.Empty<MonitorState>());

        var json = JsonSerializer.Serialize(original, _opts);
        var roundTripped = JsonSerializer.Deserialize<MonitorStatesResponse>(json, _opts);

        roundTripped!.Monitors.Should().BeEmpty();
    }

    // ── ToggleMonitorRequest ────────────────────────────────────────────────

    [Fact]
    public void ToggleMonitorRequest_RoundTripJson_PreservesFields()
    {
        var original = new ToggleMonitorRequest("etw-process", true);

        var json = JsonSerializer.Serialize(original, _opts);
        var roundTripped = JsonSerializer.Deserialize<ToggleMonitorRequest>(json, _opts);

        roundTripped.Should().Be(original);
        roundTripped!.MonitorId.Should().Be("etw-process");
        roundTripped.Enabled.Should().BeTrue();
    }

    [Fact]
    public void ToggleMonitorRequest_DisableVariant_RoundTripsCorrectly()
    {
        var original = new ToggleMonitorRequest("dns", false);

        var json = JsonSerializer.Serialize(original, _opts);
        var roundTripped = JsonSerializer.Deserialize<ToggleMonitorRequest>(json, _opts);

        roundTripped!.Enabled.Should().BeFalse();
    }

    // ── ToggleMonitorResponse ───────────────────────────────────────────────

    [Fact]
    public void ToggleMonitorResponse_Success_RoundTripJson_HasNullError()
    {
        var original = new ToggleMonitorResponse(true, null);

        var json = JsonSerializer.Serialize(original, _opts);
        var roundTripped = JsonSerializer.Deserialize<ToggleMonitorResponse>(json, _opts);

        roundTripped.Should().Be(original);
        roundTripped!.Success.Should().BeTrue();
        roundTripped.Error.Should().BeNull();
    }

    [Fact]
    public void ToggleMonitorResponse_Failure_RoundTripJson_PreservesErrorMessage()
    {
        var original = new ToggleMonitorResponse(false, "Monitor 'dns' not found");

        var json = JsonSerializer.Serialize(original, _opts);
        var roundTripped = JsonSerializer.Deserialize<ToggleMonitorResponse>(json, _opts);

        roundTripped!.Success.Should().BeFalse();
        roundTripped.Error.Should().Be("Monitor 'dns' not found");
    }

    // ── Wire format sanity ──────────────────────────────────────────────────

    [Fact]
    public void MonitorState_SerializedJson_ContainsExpectedPropertyNames()
    {
        var state = new MonitorState("dns", "DNS Protection", true, "Running", 7L);
        var json = JsonSerializer.Serialize(state, _opts);

        // Web defaults use camelCase — verify the wire keys are camelCase
        json.Should().Contain("\"id\"");
        json.Should().Contain("\"displayName\"");
        json.Should().Contain("\"enabled\"");
        json.Should().Contain("\"status\"");
        json.Should().Contain("\"eventsEmitted\"");
    }
}
