namespace Argus.Defender.Hosting;

/// <summary>
/// Contract for a real-time Defender monitor that can be started and stopped at runtime.
/// All monitors are registered in DI as <see cref="IDefenderMonitor"/> and managed by
/// <see cref="MonitorRegistry"/>.
/// </summary>
public interface IDefenderMonitor
{
    /// <summary>Stable machine identifier, e.g. "filesystem", "etw", "amsi".</summary>
    string Id { get; }

    /// <summary>Human-readable name shown in the dashboard, e.g. "File System Monitor".</summary>
    string DisplayName { get; }

    /// <summary>Whether the monitor is currently running.</summary>
    bool IsEnabled { get; }

    /// <summary>Current operational status of the monitor.</summary>
    MonitorStatus Status { get; }

    /// <summary>Cumulative count of events emitted since the monitor started.</summary>
    long EventsEmitted { get; }

    /// <summary>Start the monitor and begin emitting events.</summary>
    Task EnableAsync(CancellationToken ct);

    /// <summary>Stop the monitor and cease emitting events.</summary>
    Task DisableAsync(CancellationToken ct);
}

/// <summary>Operational status of an <see cref="IDefenderMonitor"/>.</summary>
public enum MonitorStatus
{
    /// <summary>Monitor is actively running.</summary>
    Running,

    /// <summary>Monitor is present but not running.</summary>
    Stopped,

    /// <summary>Monitor is a placeholder; the feature has not been implemented yet.</summary>
    NotImplemented,

    /// <summary>Monitor encountered a fatal error and is non-functional.</summary>
    Error,
}
