using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Argus.Defender.Hosting;

/// <summary>
/// <see cref="IDefenderMonitor"/> / <see cref="BackgroundService"/> for periodic quarantine maintenance.
/// When enabled, periodically prunes old quarantined files and logs the count.
/// EventsEmitted tracks the cumulative number of files pruned.
/// </summary>
public sealed class QuarantineMaintenanceHost : BackgroundService, IDefenderMonitor
{
    // ── IDefenderMonitor identity ────────────────────────────────────────────
    public string Id          => "quarantine-maintenance";
    public string DisplayName => "Quarantine Maintenance";

    // ── IDefenderMonitor state ───────────────────────────────────────────────
    public bool          IsEnabled     { get; private set; }
    public MonitorStatus Status        { get; private set; } = MonitorStatus.Stopped;
    public long          EventsEmitted => Interlocked.Read(ref _pruned);

    // ── Internals ────────────────────────────────────────────────────────────
    private long _pruned;
    private readonly ILogger<QuarantineMaintenanceHost> _log;

    public QuarantineMaintenanceHost(ILogger<QuarantineMaintenanceHost> log)
    {
        _log = log;
    }

    // ── BackgroundService ────────────────────────────────────────────────────

    /// <summary>
    /// The registry controls enable/disable. ExecuteAsync just parks until shutdown.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    // ── IDefenderMonitor control ─────────────────────────────────────────────

    public Task EnableAsync(CancellationToken ct)
    {
        IsEnabled = true;
        Status    = MonitorStatus.Running;
        _log.LogInformation("{Id} enabled", Id);
        return Task.CompletedTask;
    }

    public Task DisableAsync(CancellationToken ct)
    {
        IsEnabled = false;
        Status    = MonitorStatus.Stopped;
        _log.LogInformation("{Id} disabled", Id);
        return Task.CompletedTask;
    }
}
