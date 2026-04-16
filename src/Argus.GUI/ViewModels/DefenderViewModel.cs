using Argus.Core.IPC;
using Argus.GUI.IPC;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Collections.ObjectModel;
using System.Windows;

namespace Argus.GUI.ViewModels;

// ── Per-monitor card model ─────────────────────────────────────────────────────

/// <summary>
/// Observable wrapper around <see cref="MonitorState"/> for XAML binding.
/// </summary>
public sealed partial class MonitorCardViewModel : ObservableObject
{
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _status = "Unknown";
    [ObservableProperty] private long _eventsEmitted;

    public bool IsRunning => Status == "Running";

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(IsRunning));

    public void ApplyState(MonitorState state)
    {
        Id = state.Id;
        DisplayName = state.DisplayName;
        Enabled = state.Enabled;
        Status = state.Status;
        EventsEmitted = state.EventsEmitted;
    }
}

// ── DefenderViewModel ─────────────────────────────────────────────────────────

public sealed partial class DefenderViewModel : ObservableObject, IDisposable
{
    private readonly DefenderPipeBridge _bridge;
    private CancellationTokenSource? _startCts;
    private bool _disposed;

    // ── Connection state ──────────────────────────────────────────────────────

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectionLabel = "Connecting...";

    // ── Stat-card properties ──────────────────────────────────────────────────

    [ObservableProperty] private int _activeMonitorCount;
    [ObservableProperty] private string _activeMonitorDisplay = "0/0";

    // ── Filter / status bar (existing) ───────────────────────────────────────

    [ObservableProperty] private string _selectedFilter = "ALL";
    [ObservableProperty] private string _status = "Connecting to Defender service...";

    // ── Monitor card collection ───────────────────────────────────────────────

    public ObservableCollection<MonitorCardViewModel> Monitors { get; } = [];

    // ── Construction ─────────────────────────────────────────────────────────

    public DefenderViewModel(DefenderPipeBridge bridge)
    {
        _bridge = bridge;
        _bridge.StatesUpdated += OnStatesUpdated;
        _bridge.Disconnected += OnDisconnected;
    }

    // ── Page activation / deactivation ───────────────────────────────────────

    /// <summary>
    /// Call when the Defender view becomes active.
    /// Starts the bridge connection and poll loop.
    /// </summary>
    public void Activate()
    {
        if (_startCts is not null) return; // already started
        _startCts = new CancellationTokenSource();
        _ = _bridge.StartAsync(_startCts.Token);
    }

    /// <summary>
    /// Call when the Defender view is navigated away from.
    /// Stops polling to avoid unnecessary IPC traffic.
    /// </summary>
    public void Deactivate()
    {
        _startCts?.Cancel();
        _startCts?.Dispose();
        _startCts = null;
        _bridge.Stop();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void SetFilter(string filter)
    {
        SelectedFilter = filter;
        Status = filter == "ALL"
            ? "Showing all events"
            : $"Filtered to {filter.ToLower()} events";
    }

    /// <summary>
    /// Invoked by the toggle switch for a monitor card.
    /// Sends <see cref="ToggleMonitorRequest"/> and reverts on failure.
    /// </summary>
    [RelayCommand]
    private async Task ToggleMonitorAsync(MonitorCardViewModel card)
    {
        // The UI has already flipped card.Enabled — capture the new desired state
        bool desired = card.Enabled;
        string monitorId = card.Id;

        var response = await _bridge.ToggleMonitorAsync(monitorId, desired, CancellationToken.None);

        if (response is null || !response.Success)
        {
            // Revert the toggle on failure
            Log.Warning("Toggle {MonitorId} → {Desired} failed: {Error}",
                monitorId, desired, response?.Error ?? "no response");

            // Must update on UI thread
            Application.Current?.Dispatcher.Invoke(() => card.Enabled = !desired);

            Status = $"Could not toggle {card.DisplayName}: " +
                     (response?.Error ?? "Defender not responding");
        }
        else
        {
            Status = $"{card.DisplayName} {(desired ? "enabled" : "disabled")}";
            // Trigger a refresh to get the authoritative state from Defender
            // (bridge poll will pick it up within 2 s anyway, but a quick re-read here
            //  gives immediate feedback on EventsEmitted / Status text)
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnStatesUpdated(IReadOnlyList<MonitorState> states)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            IsConnected = true;
            ConnectionLabel = "Connected";

            // Sync the observable collection (add missing, update existing)
            foreach (var state in states)
            {
                var existing = Monitors.FirstOrDefault(m => m.Id == state.Id);
                if (existing is null)
                {
                    var card = new MonitorCardViewModel();
                    card.ApplyState(state);
                    Monitors.Add(card);
                }
                else
                {
                    existing.ApplyState(state);
                }
            }

            // Remove cards that are no longer reported (shouldn't happen in practice)
            var ids = states.Select(s => s.Id).ToHashSet();
            foreach (var stale in Monitors.Where(m => !ids.Contains(m.Id)).ToList())
                Monitors.Remove(stale);

            // Update aggregate stat
            ActiveMonitorCount = Monitors.Count(m => m.IsRunning);
            ActiveMonitorDisplay = $"{ActiveMonitorCount}/{Monitors.Count}";

            if (SelectedFilter == "ALL")
                Status = $"Real-time protection active — {ActiveMonitorCount}/{Monitors.Count} monitors running";
        });
    }

    private void OnDisconnected()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            IsConnected = false;
            ConnectionLabel = "Disconnected — Defender not running";
            Status = "Defender service unavailable — retrying...";
            ActiveMonitorDisplay = "—";
        });
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bridge.StatesUpdated -= OnStatesUpdated;
        _bridge.Disconnected -= OnDisconnected;
        Deactivate();
    }
}
