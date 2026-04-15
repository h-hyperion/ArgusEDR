namespace Argus.Core.IPC;

/// <summary>
/// Represents the state of a single real-time monitor in Argus.Defender.
/// Status is a string (not enum) to keep the wire format stable if MonitorStatus evolves.
/// Valid Status values: "Running", "Stopped", "NotImplemented", "Error"
/// </summary>
public record MonitorState(
    string Id,
    string DisplayName,
    bool Enabled,
    string Status,
    long EventsEmitted);

/// <summary>Request all monitor states from Argus.Defender.</summary>
public record GetMonitorStatesRequest();

/// <summary>Response carrying the state of all registered monitors.</summary>
public record MonitorStatesResponse(IReadOnlyList<MonitorState> Monitors);

/// <summary>Request to enable or disable a specific monitor by ID.</summary>
public record ToggleMonitorRequest(string MonitorId, bool Enabled);

/// <summary>Response indicating whether the toggle succeeded.</summary>
public record ToggleMonitorResponse(bool Success, string? Error);
