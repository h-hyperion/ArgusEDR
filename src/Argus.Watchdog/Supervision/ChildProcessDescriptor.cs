namespace Argus.Watchdog.Supervision;

/// <summary>
/// Immutable description of a child process that Argus.Watchdog manages.
/// </summary>
public record ChildProcessDescriptor(
    string Name,      // e.g. "Defender"
    string ExePath,   // full path to the managed executable
    string[] Args     // command-line arguments (typically empty)
);
