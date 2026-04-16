using Argus.Defender.Dns;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Argus.Defender.Hosting;

/// <summary>
/// <see cref="IDefenderMonitor"/> / <see cref="BackgroundService"/> wrapper for
/// <see cref="DnsProtectionService"/>.
///
/// DnsProtectionService is not a background monitor — it applies DNS settings on demand
/// via <c>Apply(DnsProfile)</c>. This host models enable/disable as:
///   - EnableAsync  → Apply(<see cref="DnsProfile.MalwareBlocking"/>)
///   - DisableAsync → Apply(<see cref="DnsProfile.System"/>) (resets to automatic)
///
/// EventsEmitted strategy: DNS protection is not event-driven; there is no per-event
/// counter. EventsEmitted is always 0 and is documented as such.
/// </summary>
public sealed class DnsMonitorHost : BackgroundService, IDefenderMonitor
{
    // ── IDefenderMonitor identity ────────────────────────────────────────────
    public string Id          => "dns";
    public string DisplayName => "DNS Protection";

    // ── IDefenderMonitor state ───────────────────────────────────────────────
    public bool          IsEnabled     { get; private set; }
    public MonitorStatus Status        { get; private set; } = MonitorStatus.Stopped;

    /// <summary>
    /// Always 0 — DNS protection is a one-shot setter, not an event stream.
    /// </summary>
    public long EventsEmitted => 0;

    // ── Internals ────────────────────────────────────────────────────────────
    private readonly DnsProtectionService       _inner;
    private readonly ILogger<DnsMonitorHost>    _log;

    public DnsMonitorHost(
        IDnsNativeApi           dnsApi,
        ILogger<DnsMonitorHost> log)
    {
        _inner = new DnsProtectionService(dnsApi);
        _log   = log;
    }

    // ── BackgroundService ────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { /* normal shutdown */ }
        finally { await DisableAsync(CancellationToken.None).ConfigureAwait(false); }
    }

    // ── IDefenderMonitor control ─────────────────────────────────────────────

    public Task EnableAsync(CancellationToken ct)
    {
        if (IsEnabled) return Task.CompletedTask;

        try
        {
            _inner.Apply(DnsProfile.MalwareBlocking);
            IsEnabled = true;
            Status    = MonitorStatus.Running;
            _log.LogInformation("{Id} enabled — DNS set to malware-blocking profile", Id);
        }
        catch (Exception ex)
        {
            Status = MonitorStatus.Error;
            _log.LogError(ex, "Failed to enable {Id}", Id);
            throw;
        }

        return Task.CompletedTask;
    }

    public Task DisableAsync(CancellationToken ct)
    {
        if (!IsEnabled) return Task.CompletedTask;

        try
        {
            _inner.Apply(DnsProfile.System);
            IsEnabled = false;
            Status    = MonitorStatus.Stopped;
            _log.LogInformation("{Id} disabled — DNS reset to system automatic", Id);
        }
        catch (Exception ex)
        {
            Status = MonitorStatus.Error;
            _log.LogError(ex, "Failed to disable {Id}", Id);
            throw;
        }

        return Task.CompletedTask;
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    public override void Dispose()
    {
        base.Dispose();
    }
}
