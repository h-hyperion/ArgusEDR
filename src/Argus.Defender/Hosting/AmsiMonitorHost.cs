using Microsoft.Extensions.Hosting;

namespace Argus.Defender.Hosting;

/// <summary>
/// Placeholder <see cref="IDefenderMonitor"/> / <see cref="BackgroundService"/> for AMSI script interception.
/// This is a v2.2 feature stub; the actual implementation will monitor AMSI events.
/// Currently always reports <see cref="MonitorStatus.NotImplemented"/>.
/// </summary>
public sealed class AmsiMonitorHost : BackgroundService, IDefenderMonitor
{
    // ── IDefenderMonitor identity ────────────────────────────────────────────
    public string Id          => "amsi";
    public string DisplayName => "AMSI Script Interception";

    // ── IDefenderMonitor state ───────────────────────────────────────────────
    public bool          IsEnabled { get; private set; }
    public MonitorStatus Status    => MonitorStatus.NotImplemented;
    public long          EventsEmitted => 0;

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
        return Task.CompletedTask;
    }

    public Task DisableAsync(CancellationToken ct)
    {
        IsEnabled = false;
        return Task.CompletedTask;
    }
}
