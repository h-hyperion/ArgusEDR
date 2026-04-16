using Argus.Defender.Etw;
using Argus.Defender.Monitors;   // EventPipeline
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Argus.Defender.Hosting;

/// <summary>
/// <see cref="IDefenderMonitor"/> / <see cref="BackgroundService"/> wrapper for
/// <see cref="EtwConsumer"/>.
///
/// EventsEmitted strategy: <see cref="EventPipeline"/> is sealed and already exposes a
/// <c>Processed</c> counter that increments on every successfully published event.
/// We inject the pipeline into the consumer and read <c>Processed</c> as our counter.
/// </summary>
public sealed class EtwMonitorHost : BackgroundService, IDefenderMonitor
{
    // ── IDefenderMonitor identity ────────────────────────────────────────────
    public string Id          => "etw";
    public string DisplayName => "ETW Consumer";

    // ── IDefenderMonitor state ───────────────────────────────────────────────
    public bool          IsEnabled { get; private set; }
    public MonitorStatus Status    { get; private set; } = MonitorStatus.Stopped;

    /// <summary>
    /// Proxies <see cref="EventPipeline.Processed"/> from the active pipeline.
    /// Returns 0 when the monitor is stopped (pipeline is null).
    /// </summary>
    public long EventsEmitted => _pipeline?.Processed ?? 0;

    // ── Internals ────────────────────────────────────────────────────────────
    private EtwConsumer?             _inner;
    private EventPipeline?           _pipeline;
    private CancellationTokenSource? _innerCts;
    private Task?                    _innerTask;

    private readonly ILogger<EtwMonitorHost> _log;

    public EtwMonitorHost(ILogger<EtwMonitorHost> log)
    {
        _log = log;
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
            _innerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _pipeline = new EventPipeline();
            _inner    = new EtwConsumer(pipeline: _pipeline);
            _innerTask = _inner.StartAsync(_innerCts.Token);

            IsEnabled = true;
            Status    = MonitorStatus.Running;
            _log.LogInformation("{Id} enabled", Id);
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

            if (_innerTask is not null)
            {
                try { await _innerTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { /* expected */ }
                _innerTask = null;
            }

            _inner?.Dispose();
            _inner = null;

            _pipeline?.Dispose();
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

    // ── Dispose ──────────────────────────────────────────────────────────────

    public override void Dispose()
    {
        _innerCts?.Dispose();
        _inner?.Dispose();
        _pipeline?.Dispose();
        base.Dispose();
    }
}
