namespace Argus.Watchdog.Supervision;

using System.Diagnostics;
using System.Text.Json;
using Argus.Core;
using Argus.Core.Supervision;
using Microsoft.Extensions.Logging;

// ── Process launcher abstraction ─────────────────────────────────────────────

/// <summary>
/// Abstraction over <see cref="Process.Start"/> so tests can inject a fake.
/// </summary>
public interface IProcessLauncher
{
    IRunningProcess Launch(string exePath, string[] args);
}

/// <summary>
/// Represents a running process with a readable stdout stream.
/// </summary>
public interface IRunningProcess : IAsyncDisposable
{
    /// <summary>Read one UTF-8 line from stdout; returns null at EOF.</summary>
    Task<string?> ReadLineAsync(CancellationToken ct);

    /// <summary>Signals the process to exit gracefully (closes its stdin).</summary>
    void RequestGracefulStop();

    /// <summary>Force-terminates the process immediately.</summary>
    void Kill();

    /// <summary>Waits up to <paramref name="timeout"/> for the process to exit.</summary>
    Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken ct);
}

// ── Real implementations ──────────────────────────────────────────────────────

internal sealed class RealProcessLauncher : IProcessLauncher
{
    public IRunningProcess Launch(string exePath, string[] args)
    {
        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = false,
            CreateNoWindow         = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Process.Start returned null for '{exePath}'");

        return new RealRunningProcess(proc);
    }
}

internal sealed class RealRunningProcess(Process process) : IRunningProcess
{
    public Task<string?> ReadLineAsync(CancellationToken ct) =>
        process.StandardOutput.ReadLineAsync(ct).AsTask();

    public void RequestGracefulStop() => process.StandardInput.Close();

    public void Kill()
    {
        try { process.Kill(entireProcessTree: true); }
        catch { /* already exited */ }
    }

    public async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        process.Dispose();
        return ValueTask.CompletedTask;
    }
}

// ── ManagedChildProcess ───────────────────────────────────────────────────────

/// <summary>
/// Supervises a single child process: starts it, monitors its heartbeat,
/// restarts it with exponential backoff on failure, and transitions to SafeMode
/// after too many consecutive failures.
/// </summary>
public sealed class ManagedChildProcess : IAsyncDisposable
{
    private readonly ChildProcessDescriptor _descriptor;
    private readonly ManifestVerifier       _verifier;
    private readonly RestartPolicy          _restartPolicy;
    private readonly ILogger<ManagedChildProcess> _log;
    private readonly string                 _manifestPath;
    private readonly IProcessLauncher       _launcher;

    /// <summary>
    /// Injectable delay function. Defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
    /// Tests may replace this with an instant-return stub to avoid real backoff waits.
    /// </summary>
    internal Func<TimeSpan, CancellationToken, Task> DelayFunc { get; set; } =
        Task.Delay;

    private IRunningProcess?            _process;
    private DateTimeOffset              _lastHeartbeat;
    private CancellationTokenSource?   _cts;
    private Task?                       _supervisionLoop;

    /// <summary>Current lifecycle state of the managed child process.</summary>
    public ChildState State { get; private set; } = ChildState.NotStarted;

    /// <summary>
    /// Raised whenever <see cref="State"/> changes.
    /// The handler receives this instance and the new state.
    /// </summary>
    public event Action<ManagedChildProcess, ChildState>? StateChanged;

    public ManagedChildProcess(
        ChildProcessDescriptor descriptor,
        ManifestVerifier       verifier,
        RestartPolicy          restartPolicy,
        string                 manifestPath,
        ILogger<ManagedChildProcess> log,
        IProcessLauncher?      launcher = null)
    {
        _descriptor    = descriptor;
        _verifier      = verifier;
        _restartPolicy = restartPolicy;
        _manifestPath  = manifestPath;
        _log           = log;
        _launcher      = launcher ?? new RealProcessLauncher();
    }

    // ── Public control ────────────────────────────────────────────────────────

    /// <summary>Starts the supervision loop.</summary>
    public Task StartAsync(CancellationToken ct)
    {
        _cts             = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _supervisionLoop = RunSupervisionLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gracefully stops the child process and the supervision loop,
    /// waiting up to <paramref name="gracePeriod"/> before force-killing.
    /// </summary>
    public async Task StopAsync(TimeSpan gracePeriod)
    {
        if (_cts is null) return;

        await _cts.CancelAsync().ConfigureAwait(false);

        // Snapshot the field to avoid a TOCTOU race: the supervision loop
        // runs concurrently and may null _process between the null-guard and
        // the subsequent method call.
        var snapshot = _process;
        snapshot?.RequestGracefulStop();

        if (snapshot is not null)
        {
            var exited = await snapshot.WaitForExitAsync(gracePeriod, CancellationToken.None)
                .ConfigureAwait(false);
            if (!exited)
            {
                _log.LogWarning("[{Name}] Grace period elapsed; force-killing.", _descriptor.Name);
                snapshot.Kill();
            }
        }

        if (_supervisionLoop is not null)
        {
            try { await _supervisionLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }
    }

    // ── Supervision loop ──────────────────────────────────────────────────────

    private async Task RunSupervisionLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // 1. Manifest verification (skip when manifest doesn't exist yet — first run)
            if (File.Exists(_manifestPath))
            {
                bool verified;
                try
                {
                    verified = _verifier.Verify(_descriptor.ExePath, _manifestPath);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex,
                        "[{Name}] Manifest verification threw; entering SafeMode.", _descriptor.Name);
                    TransitionTo(ChildState.SafeMode);
                    return;
                }

                if (!verified)
                {
                    _log.LogError(
                        "[{Name}] Hash mismatch against manifest; entering SafeMode.", _descriptor.Name);
                    TransitionTo(ChildState.SafeMode);
                    return;
                }
            }

            // 2. Launch
            TransitionTo(ChildState.Starting);
            _lastHeartbeat = DateTimeOffset.UtcNow; // grace window at startup

            IRunningProcess proc;
            try
            {
                proc = _launcher.Launch(_descriptor.ExePath, _descriptor.Args);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[{Name}] Failed to launch process.", _descriptor.Name);
                await HandleFailureAndMaybeDelayAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested || State == ChildState.SafeMode) return;
                continue;
            }

            _process = proc;

            // 3. Run stdout reader + heartbeat watchdog concurrently
            using var processCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var stdoutTask   = ReadStdoutLoopAsync(proc, processCts.Token);
            var watchdogTask = HeartbeatWatchdogAsync(proc, processCts.Token);

            // Wait for whichever task completes first (process EOF or watchdog kill)
            await Task.WhenAny(stdoutTask, watchdogTask).ConfigureAwait(false);

            // Cancel the other task
            await processCts.CancelAsync().ConfigureAwait(false);

            try { await Task.WhenAll(stdoutTask, watchdogTask).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[{Name}] Exception in supervision sub-tasks.", _descriptor.Name);
            }

            await proc.DisposeAsync().ConfigureAwait(false);
            _process = null;

            // 4. If shutdown was requested, exit cleanly
            if (ct.IsCancellationRequested) return;

            // 5. Restart flow
            await HandleFailureAndMaybeDelayAsync(ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested || State == ChildState.SafeMode) return;
        }
    }

    private async Task ReadStdoutLoopAsync(IRunningProcess proc, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line = await proc.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break; // EOF — process exited

                if (TryParseHeartbeat(line, out var frame) && frame is not null)
                {
                    OnHeartbeatReceived(frame.Timestamp);
                }
                else
                {
                    _log.LogInformation("[{Name}] stdout: {Line}", _descriptor.Name, line);
                }
            }
        }
        catch (OperationCanceledException) { /* expected */ }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[{Name}] stdout reader exited with exception.", _descriptor.Name);
        }
    }

    private async Task HeartbeatWatchdogAsync(IRunningProcess proc, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(ArgusConstants.HeartbeatTimeoutSeconds);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);

                if (DateTimeOffset.UtcNow - _lastHeartbeat > timeout)
                {
                    _log.LogWarning(
                        "[{Name}] Heartbeat timeout ({Seconds}s); killing process.",
                        _descriptor.Name, ArgusConstants.HeartbeatTimeoutSeconds);
                    proc.Kill();
                    return;
                }
            }
        }
        catch (OperationCanceledException) { /* expected */ }
    }

    private async Task HandleFailureAndMaybeDelayAsync(CancellationToken ct)
    {
        _restartPolicy.RegisterFailure();

        if (_restartPolicy.ShouldTriggerSafeMode)
        {
            _log.LogError(
                "[{Name}] Reached {Max} consecutive failures; entering SafeMode.",
                _descriptor.Name, ArgusConstants.MaxConsecutiveRestartFailures);
            TransitionTo(ChildState.SafeMode);
            return;
        }

        var delay = _restartPolicy.NextDelay;
        _log.LogWarning(
            "[{Name}] Restart in {Delay}s (failure #{Count}).",
            _descriptor.Name, delay.TotalSeconds, _restartPolicy.ConsecutiveFailures);
        TransitionTo(ChildState.Restarting);

        try { await DelayFunc(delay, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { /* shutdown requested during backoff delay */ }
    }

    private void TransitionTo(ChildState newState)
    {
        if (State == newState) return;
        State = newState;
        StateChanged?.Invoke(this, newState);
    }

    // ── Testable seams ────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to deserialise <paramref name="line"/> as a <see cref="HeartbeatFrame"/>.
    /// Returns <c>true</c> when the JSON is valid and <c>Type == "heartbeat"</c>.
    /// </summary>
    internal bool TryParseHeartbeat(string line, out HeartbeatFrame? frame)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            // Guard: must have a "Type" property equal to "heartbeat" in the JSON itself.
            // (HeartbeatFrame.Type is a computed property — it's always "heartbeat" on any
            //  deserialized instance, so we cannot rely on it to discriminate other message types.)
            if (!root.TryGetProperty("Type", out var typeProp) ||
                !string.Equals(typeProp.GetString(), "heartbeat", StringComparison.OrdinalIgnoreCase))
            {
                frame = null;
                return false;
            }

            frame = JsonSerializer.Deserialize<HeartbeatFrame>(line);
            return frame is not null;
        }
        catch
        {
            frame = null;
            return false;
        }
    }

    /// <summary>
    /// Called when a valid heartbeat line arrives.
    /// Updates the last-seen timestamp and on the first heartbeat transitions
    /// <see cref="State"/> from <see cref="ChildState.Starting"/> to
    /// <see cref="ChildState.Running"/> and calls
    /// <see cref="RestartPolicy.RegisterSuccess"/>.
    /// </summary>
    internal void OnHeartbeatReceived(DateTimeOffset timestamp)
    {
        _lastHeartbeat = timestamp;

        if (State == ChildState.Starting)
        {
            _restartPolicy.RegisterSuccess();
            TransitionTo(ChildState.Running);
        }
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await StopAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        _cts?.Dispose();
    }
}
