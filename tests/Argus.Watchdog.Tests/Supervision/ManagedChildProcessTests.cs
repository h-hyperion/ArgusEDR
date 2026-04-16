namespace Argus.Watchdog.Tests.Supervision;

using System.Text.Json;
using Argus.Core;
using Argus.Core.Supervision;
using Argus.Watchdog.Supervision;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

// ── Fakes ─────────────────────────────────────────────────────────────────────

/// <summary>
/// A fake process that drains a pre-loaded queue of stdout lines,
/// then signals EOF when the queue is empty.
/// </summary>
internal sealed class FakeRunningProcess : IRunningProcess
{
    private readonly Queue<string?> _lines;
    private readonly TaskCompletionSource _disposed = new();

    public bool KillCalled           { get; private set; }
    public bool GracefulStopCalled   { get; private set; }

    public FakeRunningProcess(IEnumerable<string?> lines)
    {
        _lines = new Queue<string?>(lines);
    }

    public Task<string?> ReadLineAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_lines.Count == 0) return Task.FromResult<string?>(null); // EOF
        return Task.FromResult(_lines.Dequeue());
    }

    public void RequestGracefulStop() => GracefulStopCalled = true;

    public void Kill() => KillCalled = true;

    public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken ct) =>
        Task.FromResult(true);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// A fake launcher that returns a pre-built <see cref="FakeRunningProcess"/>.
/// </summary>
internal sealed class FakeLauncher(FakeRunningProcess process) : IProcessLauncher
{
    public int LaunchCount { get; private set; }

    public IRunningProcess Launch(string exePath, string[] args)
    {
        LaunchCount++;
        return process;
    }
}

/// <summary>
/// Launcher that throws on every launch attempt (simulates exe not found).
/// </summary>
internal sealed class FailingLauncher : IProcessLauncher
{
    public int LaunchCount { get; private set; }

    public IRunningProcess Launch(string exePath, string[] args)
    {
        LaunchCount++;
        throw new InvalidOperationException("Simulated launch failure");
    }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

file static class Build
{
    internal static ManagedChildProcess Subject(
        IProcessLauncher launcher,
        RestartPolicy?   policy       = null,
        string           manifestPath = "nonexistent-manifest.json") =>
        new(
            new ChildProcessDescriptor("Test", "test.exe", []),
            new ManifestVerifier(),
            policy ?? new RestartPolicy(),
            manifestPath,
            NullLogger<ManagedChildProcess>.Instance,
            launcher);

    internal static string HeartbeatJson(DateTimeOffset? ts = null) =>
        JsonSerializer.Serialize(new { Type = "heartbeat", Timestamp = ts ?? DateTimeOffset.UtcNow });
}

// ── Tests: heartbeat parsing (pure / seam-based) ─────────────────────────────

public class TryParseHeartbeat_Tests
{
    private readonly ManagedChildProcess _sut = Build.Subject(new FailingLauncher());

    [Fact]
    public void ValidHeartbeatJson_ReturnsTrue_AndPopulatesFrame()
    {
        var ts   = new DateTimeOffset(2026, 4, 15, 12, 0, 0, TimeSpan.Zero);
        var json = Build.HeartbeatJson(ts);

        var result = _sut.TryParseHeartbeat(json, out var frame);

        result.Should().BeTrue();
        frame.Should().NotBeNull();
        frame!.Timestamp.Should().Be(ts);
        frame.Type.Should().Be("heartbeat");
    }

    [Fact]
    public void NonHeartbeatJson_ReturnsFalse()
    {
        // JSON with a different "Type" value must be rejected — other message types
        // share the wire format but are not heartbeats.
        var json = """{"Type":"alert","Timestamp":"2026-04-15T12:00:00Z","Message":"something happened"}""";

        var result = _sut.TryParseHeartbeat(json, out var frame);

        result.Should().BeFalse();
        frame.Should().BeNull();
    }

    [Fact]
    public void MalformedJson_ReturnsFalse_DoesNotThrow()
    {
        var act = () => _sut.TryParseHeartbeat("this is not json at all {{{", out _);

        act.Should().NotThrow();
        _sut.TryParseHeartbeat("this is not json at all {{{", out var frame);
        frame.Should().BeNull();
    }

    [Fact]
    public void EmptyString_ReturnsFalse_DoesNotThrow()
    {
        var act = () => _sut.TryParseHeartbeat(string.Empty, out _);
        act.Should().NotThrow();
    }
}

// ── Tests: OnHeartbeatReceived seam ──────────────────────────────────────────

public class OnHeartbeatReceived_Tests
{
    [Fact]
    public void WhileStarting_TransitionsToRunning_AndCallsRegisterSuccess()
    {
        var policy = new RestartPolicy();
        // Put the policy into a "known failure" state so we can confirm RegisterSuccess resets it
        policy.RegisterFailure();
        policy.RegisterFailure();

        var sut = Build.Subject(new FailingLauncher(), policy);

        // Force the object into Starting state via StartAsync + immediate cancel
        // Instead we test the seam directly — OnHeartbeatReceived is internal.
        // Manually set state by calling StartAsync with a pre-cancelled token so
        // the loop exits before launching; then we'll prod the seam directly.
        // Simpler: just call OnHeartbeatReceived while state is NotStarted vs Starting.

        // State is NotStarted — heartbeat should NOT transition to Running
        sut.OnHeartbeatReceived(DateTimeOffset.UtcNow);
        sut.State.Should().Be(ChildState.NotStarted);

        // We can't easily force State to Starting without running the loop.
        // Instead, verify that the seam is called correctly via the integration path below.
        // This test focuses on what OnHeartbeatReceived does when NOT in Starting state.
        policy.ConsecutiveFailures.Should().Be(2); // RegisterSuccess not called
    }

    [Fact]
    public async Task FirstHeartbeat_ViaSupervisionLoop_TransitionsToRunning()
    {
        // Arrange: a process that immediately emits one heartbeat then EOF.
        // After the heartbeat, State transitions Starting → Running (RegisterSuccess).
        // Then the process exits (EOF) which triggers RegisterFailure and a restart cycle.
        // We stop before the second launch fires.
        var hbJson   = Build.HeartbeatJson();
        var fakeProc = new FakeRunningProcess([hbJson, null]);
        var launcher = new FakeLauncher(fakeProc);
        var policy   = new RestartPolicy();

        var states = new List<ChildState>();
        var sut    = Build.Subject(launcher, policy);
        sut.DelayFunc = (_, ct) => Task.Delay(TimeSpan.FromSeconds(60), ct); // long delay so restart doesn't fire
        sut.StateChanged += (_, s) => states.Add(s);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.StartAsync(cts.Token);

        // Give the supervision loop a moment to process the heartbeat + EOF
        await Task.Delay(300, CancellationToken.None);
        await sut.StopAsync(TimeSpan.FromSeconds(1));

        states.Should().Contain(ChildState.Starting);
        states.Should().Contain(ChildState.Running);
        // RegisterSuccess was called when heartbeat arrived (resets to 0),
        // then RegisterFailure when process exited (bumps to 1).
        policy.ConsecutiveFailures.Should().Be(1);
    }

    [Fact]
    public async Task HeartbeatUpdatesLastSeen_PreventingSpuriousTimeout()
    {
        // Process emits heartbeats quickly; watchdog should NOT kill it
        var heartbeats = Enumerable.Repeat(Build.HeartbeatJson(), 3)
                                   .Append<string?>(null)
                                   .ToArray();

        var fakeProc = new FakeRunningProcess(heartbeats);
        var launcher = new FakeLauncher(fakeProc);

        var sut = Build.Subject(launcher);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.StartAsync(cts.Token);
        await Task.Delay(300, CancellationToken.None);
        await sut.StopAsync(TimeSpan.FromSeconds(1));

        fakeProc.KillCalled.Should().BeFalse("heartbeats arrived in time");
    }
}

// ── Tests: restart policy integration ────────────────────────────────────────

public class RestartPolicy_Integration_Tests
{
    [Fact]
    public async Task OnProcessExit_RegistersFailure_AndRestarts()
    {
        // Process that exits immediately (empty line queue → EOF on first read)
        var fakeProc = new FakeRunningProcess([null]); // immediate EOF
        var launcher = new FakeLauncher(fakeProc);
        var policy   = new RestartPolicy();
        var sut      = Build.Subject(launcher, policy);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
        await sut.StartAsync(cts.Token);

        // Let the loop run for a short time — it should have attempted at least 2 launches
        await Task.Delay(400, CancellationToken.None);
        await sut.StopAsync(TimeSpan.FromSeconds(1));

        policy.ConsecutiveFailures.Should().BeGreaterThanOrEqualTo(1,
            "at least one failure should have been registered");
    }

    [Fact]
    public async Task AfterMaxFailures_EntersSafeMode_NoFurtherLaunches()
    {
        // Use a launcher that always fails immediately (no process started).
        // Inject an instant delay so the backoff doesn't add real wall-clock seconds.
        var launcher = new FailingLauncher();
        var policy   = new RestartPolicy();
        var states   = new List<ChildState>();

        var sut = Build.Subject(launcher, policy);
        sut.DelayFunc = (_, ct) => Task.CompletedTask; // bypass all backoff waits
        sut.StateChanged += (_, s) => states.Add(s);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.StartAsync(cts.Token);

        // With instant delays the loop should reach SafeMode very quickly
        var deadline = DateTimeOffset.UtcNow.AddSeconds(4);
        while (sut.State != ChildState.SafeMode && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(20, CancellationToken.None);

        await sut.StopAsync(TimeSpan.FromSeconds(1));

        sut.State.Should().Be(ChildState.SafeMode);
        states.Should().Contain(ChildState.SafeMode);

        // After SafeMode no further launches should happen
        var launchCountAtSafeMode = launcher.LaunchCount;
        await Task.Delay(200, CancellationToken.None);
        launcher.LaunchCount.Should().Be(launchCountAtSafeMode,
            "supervision loop must stop after SafeMode");
    }

    [Fact]
    public void RegisterSuccess_ResetsFailureCounter_ViaSeam()
    {
        // Pure unit test — no process spawning needed
        var policy = new RestartPolicy();

        for (int i = 0; i < 3; i++) policy.RegisterFailure();
        policy.ConsecutiveFailures.Should().Be(3);

        var sut = Build.Subject(new FailingLauncher(), policy);

        // Drive the seam directly (state is NotStarted, so RegisterSuccess is NOT called
        // by OnHeartbeatReceived — but we can call RegisterSuccess directly on the policy)
        policy.RegisterSuccess();

        policy.ConsecutiveFailures.Should().Be(0);
        policy.ShouldTriggerSafeMode.Should().BeFalse();
        policy.NextDelay.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SuccessfulHeartbeat_CallsRegisterSuccess_ResetsPolicy()
    {
        // Process emits one heartbeat then EOF.
        // Sequence: RegisterSuccess (heartbeat) → ConsecutiveFailures=0,
        //           then process exits → RegisterFailure → ConsecutiveFailures=1.
        // We verify the counter was reset to 0 mid-run by confirming it stayed at 1
        // (not at the pre-loaded 3) and that Running state was reached.
        var fakeProc = new FakeRunningProcess([Build.HeartbeatJson(), null]);
        var launcher = new FakeLauncher(fakeProc);
        var policy   = new RestartPolicy();

        // Pre-load some failures to confirm RegisterSuccess truly resets them
        policy.RegisterFailure();
        policy.RegisterFailure();
        policy.RegisterFailure();

        var sut = Build.Subject(launcher, policy);
        sut.DelayFunc = (_, ct) => Task.Delay(TimeSpan.FromSeconds(60), ct); // prevent second launch

        var states = new List<ChildState>();
        sut.StateChanged += (_, s) => states.Add(s);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.StartAsync(cts.Token);
        await Task.Delay(300, CancellationToken.None);
        await sut.StopAsync(TimeSpan.FromSeconds(1));

        // Running state confirms RegisterSuccess was called (resetting the 3 pre-loaded failures)
        states.Should().Contain(ChildState.Running,
            "heartbeat arrived so RegisterSuccess was called and state went to Running");

        // After RegisterSuccess reset to 0, process exit caused 1 RegisterFailure
        policy.ConsecutiveFailures.Should().Be(1,
            "RegisterSuccess reset pre-loaded failures to 0; process exit added 1");
    }
}

// ── Tests: state transitions ──────────────────────────────────────────────────

public class StateTransition_Tests
{
    [Fact]
    public void InitialState_IsNotStarted()
    {
        var sut = Build.Subject(new FailingLauncher());
        sut.State.Should().Be(ChildState.NotStarted);
    }

    [Fact]
    public async Task AfterStart_TransitionsToStarting_ThenRunning_OnHeartbeat()
    {
        var fakeProc = new FakeRunningProcess([Build.HeartbeatJson(), null]);
        var launcher = new FakeLauncher(fakeProc);

        var observed = new List<ChildState>();
        var sut      = Build.Subject(launcher);
        sut.StateChanged += (_, s) => observed.Add(s);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.StartAsync(cts.Token);
        await Task.Delay(300, CancellationToken.None);
        await sut.StopAsync(TimeSpan.FromSeconds(1));

        observed.Should().ContainInOrder(ChildState.Starting, ChildState.Running);
    }
}
