using Argus.Core;
using Argus.Core.IPC;
using Argus.GUI.IPC;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace Argus.GUI.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly GuiPipeBridge _pipeBridge;
    private readonly DefenderPipeBridge _defenderBridge;

    // ── Hysteresis thresholds ──────────────────────────────────────────────────
    // Brief glitches (< GracePeriod) are invisible to the user — status holds.
    // Sustained outages escalate: first "Reconnecting", then "Disconnected".
    // Tuned for real-world Defender restart cadence: a full ETW re-init + YARA
    // rule recompile + IPC handshake can realistically take 20-40s. The grace
    // period swallows that without alarming the user; "Reconnecting" appears
    // only once we're past the optimistic window; "Disconnected" is reserved
    // for genuinely stuck states (90s+ without a heartbeat).
    private static readonly TimeSpan GracePeriod        = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ReconnectingWindow = TimeSpan.FromSeconds(90);

    private DateTimeOffset _defenderLastContact = DateTimeOffset.UtcNow;
    private readonly DispatcherTimer _statusTimer;

    [ObservableProperty] private int _threatsDetected;
    [ObservableProperty] private int _filesScanned;
    [ObservableProperty] private int _quarantinedItems;
    [ObservableProperty] private string _protectionStatus = "Connecting...";
    [ObservableProperty] private string _lastScanTime = "Never";
    [ObservableProperty] private string _watchdogStatus = "—";
    [ObservableProperty] private string _defenderStatus = "—";
    [ObservableProperty] private string _serviceLabel = "Service: connecting...";

    /// <summary>
    /// "X/Y" display driven by live Defender pipe data.
    /// Falls back to "—" when Defender is not connected.
    /// </summary>
    [ObservableProperty] private string _activeMonitors = "—";

    public DashboardViewModel(GuiPipeBridge pipeBridge, DefenderPipeBridge defenderBridge)
    {
        _pipeBridge = pipeBridge;
        _defenderBridge = defenderBridge;
        _pipeBridge.StatusUpdated += OnStatusUpdated;
        _defenderBridge.StatesUpdated += OnDefenderStatesUpdated;
        _defenderBridge.Disconnected += OnDefenderDisconnected;

        // Tick every second to apply hysteresis to ProtectionStatus.
        // DispatcherTimer runs on the UI thread, so no Dispatcher.Invoke needed.
        _statusTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _statusTimer.Tick += OnStatusTimerTick;
        _statusTimer.Start();
    }

    // ── Timer tick — staged degradation ───────────────────────────────────────

    private void OnStatusTimerTick(object? sender, EventArgs e)
    {
        var elapsed = DateTimeOffset.UtcNow - _defenderLastContact;

        // Within grace period: hold whatever status last contact reported.
        if (elapsed < GracePeriod) return;

        // Sustained outage: escalate through Reconnecting → Disconnected.
        ProtectionStatus = elapsed < ReconnectingWindow ? "Reconnecting" : "Disconnected";

        // Mirror into DefenderStatus so the Daily Briefing dialog stays consistent.
        DefenderStatus = ProtectionStatus;
    }

    // ── Data handlers ──────────────────────────────────────────────────────────

    private void OnStatusUpdated(ServiceStatus status)
    {
        // ProtectionStatus is NOT set here — DefenderPipeBridge is the single
        // source of truth. The Watchdog's DefenderActive is "process alive"
        // (coarser signal, different poll rate) and would cause flapping.
        ThreatsDetected = status.ThreatsDetected;
        FilesScanned    = status.FilesScanned;
        QuarantinedItems = status.QuarantinedItems;
        LastScanTime    = status.LastScanTime?.ToString("MMM dd, yyyy HH:mm") ?? "Never";
        WatchdogStatus  = status.WatchdogStatus ?? "—";
        ServiceLabel    = status.ServiceRunning ? "Service: running" : "Service: stopped";
    }

    private void OnDefenderStatesUpdated(IReadOnlyList<MonitorState> states)
    {
        // Fresh contact — reset the hysteresis clock.
        _defenderLastContact = DateTimeOffset.UtcNow;

        var running = states.Count(s => s.Status == "Running");
        var total   = states.Count;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            ActiveMonitors   = $"{running}/{total}";
            DefenderStatus   = running > 0 ? "Active" : "Inactive";
            ProtectionStatus = running > 0 ? "Active" : "Inactive";
        });
    }

    private void OnDefenderDisconnected()
    {
        // Don't change ProtectionStatus here — let the timer handle degradation
        // at the right pace. Only clear the monitor count immediately.
        Application.Current?.Dispatcher.Invoke(() =>
        {
            ActiveMonitors = "—";
        });
    }
}
