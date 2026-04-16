using Argus.Core;
using Argus.Core.IPC;
using Argus.GUI.IPC;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;

namespace Argus.GUI.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly GuiPipeBridge _pipeBridge;
    private readonly DefenderPipeBridge _defenderBridge;

    [ObservableProperty] private int _threatsDetected;
    [ObservableProperty] private int _filesScanned;
    [ObservableProperty] private int _quarantinedItems;
    [ObservableProperty] private string _protectionStatus = "Offline";
    [ObservableProperty] private string _lastScanTime = "Never";
    [ObservableProperty] private string _watchdogStatus = "—";
    [ObservableProperty] private string _defenderStatus = "—";
    [ObservableProperty] private string _serviceLabel = "Service: connecting...";

    /// <summary>
    /// "X/7" display driven by live Defender pipe data.
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
    }

    private void OnStatusUpdated(ServiceStatus status)
    {
        ProtectionStatus = status.DefenderActive ? "Active" : "Inactive";
        ThreatsDetected = status.ThreatsDetected;
        FilesScanned = status.FilesScanned;
        QuarantinedItems = status.QuarantinedItems;
        LastScanTime = status.LastScanTime?.ToString("MMM dd, yyyy HH:mm") ?? "Never";
        WatchdogStatus = status.WatchdogStatus ?? "—";
        DefenderStatus = status.DefenderStatus ?? "—";
        ServiceLabel = status.ServiceRunning ? "Service: running" : "Service: stopped";
    }

    private void OnDefenderStatesUpdated(IReadOnlyList<MonitorState> states)
    {
        var running = states.Count(s => s.Status == "Running");
        var total   = states.Count;

        Application.Current?.Dispatcher.Invoke(
            () => ActiveMonitors = $"{running}/{total}");
    }

    private void OnDefenderDisconnected()
    {
        Application.Current?.Dispatcher.Invoke(
            () => ActiveMonitors = "—");
    }
}
