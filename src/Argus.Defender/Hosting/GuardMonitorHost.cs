using Argus.Defender.Guard;
using Argus.Defender.Monitors;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Argus.Defender.Hosting;

/// <summary>
/// <see cref="IDefenderMonitor"/> / <see cref="BackgroundService"/> wrapper for
/// <see cref="GuardMonitor"/>.
///
/// GuardMonitor has no StartAsync — it is a stateless callback receiver. This host:
///   1. On <see cref="EnableAsync"/>: constructs a <see cref="GuardMonitor"/> and starts a
///      background loop that drains an <see cref="EventPipeline"/> and dispatches registry
///      events to <see cref="GuardMonitor.OnRegistryChange"/>.
///   2. On <see cref="DisableAsync"/>: cancels the loop and disposes resources.
///
/// EventsEmitted strategy: incremented each time a registry-change event is dispatched
/// to the inner monitor (i.e. each call to OnRegistryChange that passes the guard's filter).
/// We increment on every event sent to OnRegistryChange, not just ones that trigger re-enforcement.
///
/// GuardConfig loading: tries <paramref name="guardConfigPath"/> first (runtime path in
/// ProgramData). If the file does not exist, falls back to the bundled GuardConfig.json
/// in the assembly's output directory.
/// </summary>
public sealed class GuardMonitorHost : BackgroundService, IDefenderMonitor
{
    // ── IDefenderMonitor identity ────────────────────────────────────────────
    public string Id          => "guard";
    public string DisplayName => "Privacy Guard";

    // ── IDefenderMonitor state ───────────────────────────────────────────────
    public bool          IsEnabled     { get; private set; }
    public MonitorStatus Status        { get; private set; } = MonitorStatus.Stopped;
    public long          EventsEmitted => Interlocked.Read(ref _eventsEmitted);

    // ── Internals ────────────────────────────────────────────────────────────
    private long                     _eventsEmitted;
    private GuardMonitor?            _inner;
    private EventPipeline?           _pipeline;
    private CancellationTokenSource? _innerCts;
    private Task?                    _drainTask;

    private readonly IWindowsPrivacyApi             _privacyApi;
    private readonly string                         _guardConfigPath;
    private readonly ILogger<GuardMonitorHost>      _log;

    public GuardMonitorHost(
        IWindowsPrivacyApi          privacyApi,
        string                      guardConfigPath,
        ILogger<GuardMonitorHost>   log)
    {
        _privacyApi      = privacyApi;
        _guardConfigPath = guardConfigPath;
        _log             = log;
    }

    // ── BackgroundService ────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { /* normal shutdown */ }
        finally { await DisableAsync(CancellationToken.None).ConfigureAwait(false); }
    }

    // ── IDefenderMonitor control ─────────────────────────────────────────────

    public async Task EnableAsync(CancellationToken ct)
    {
        if (IsEnabled) return;

        try
        {
            var config   = LoadGuardConfig();
            var enforcer = new GuardEnforcer(_privacyApi);
            _inner       = new GuardMonitor(enforcer, config);
            _pipeline    = new EventPipeline();
            _innerCts    = CancellationTokenSource.CreateLinkedTokenSource(ct);

            _drainTask = DrainPipelineAsync(_innerCts.Token);

            IsEnabled = true;
            Status    = MonitorStatus.Running;
            _log.LogInformation("{Id} enabled — {Keys} protected registry keys",
                Id, _inner.ProtectedKeyCount);
        }
        catch (Exception ex)
        {
            Status = MonitorStatus.Error;
            _log.LogError(ex, "Failed to enable {Id}", Id);
            throw;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task DisableAsync(CancellationToken ct)
    {
        if (!IsEnabled) return;

        try
        {
            _innerCts?.Cancel();
            _pipeline?.Dispose(); // signals the channel writer complete → DrainPipelineAsync exits

            if (_drainTask is not null)
            {
                try { await _drainTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { /* expected */ }
                _drainTask = null;
            }

            _inner    = null;
            _pipeline = null;
            _innerCts?.Dispose();
            _innerCts = null;

            IsEnabled = false;
            Status    = MonitorStatus.Stopped;
            _log.LogInformation("{Id} disabled", Id);
        }
        catch (Exception ex)
        {
            Status = MonitorStatus.Error;
            _log.LogError(ex, "Failed to disable {Id}", Id);
            throw;
        }
    }

    // ── Pipeline drain loop ──────────────────────────────────────────────────

    /// <summary>
    /// Reads RegistryChanged events from the pipeline and forwards them to the inner monitor.
    /// </summary>
    private async Task DrainPipelineAsync(CancellationToken ct)
    {
        if (_pipeline is null || _inner is null) return;

        var reader = _pipeline.Reader;
        try
        {
            await foreach (var evt in reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (evt.Type == MonitorEventType.RegistryChanged)
                {
                    _inner.OnRegistryChange(evt);
                    Interlocked.Increment(ref _eventsEmitted);
                }
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private GuardConfig LoadGuardConfig()
    {
        // Prefer the runtime config in ProgramData (user may have customized it).
        if (File.Exists(_guardConfigPath))
        {
            _log.LogDebug("Loading GuardConfig from runtime path: {Path}", _guardConfigPath);
            return GuardConfig.Load(_guardConfigPath);
        }

        // Fall back to the bundled default next to the binary.
        var bundled = Path.Combine(
            AppContext.BaseDirectory, "Guard", "GuardConfig.json");

        if (File.Exists(bundled))
        {
            _log.LogDebug("Loading GuardConfig from bundled path: {Path}", bundled);
            return GuardConfig.Load(bundled);
        }

        _log.LogWarning("GuardConfig not found at '{Runtime}' or '{Bundled}'; using empty config",
            _guardConfigPath, bundled);
        return new GuardConfig();
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    public override void Dispose()
    {
        _innerCts?.Dispose();
        _pipeline?.Dispose();
        base.Dispose();
    }
}
