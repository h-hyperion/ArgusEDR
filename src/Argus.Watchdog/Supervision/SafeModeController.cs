namespace Argus.Watchdog.Supervision;

using Argus.Core;

/// <summary>
/// Reads and writes the safe-mode sentinel file.
/// When the sentinel exists the Watchdog will not spawn any child processes.
/// </summary>
public sealed class SafeModeController
{
    private readonly string _sentinelPath;

    public SafeModeController(string? sentinelPath = null)
    {
        _sentinelPath = sentinelPath
            ?? ArgusConstants.SafeModeSentinelPath;
    }

    /// <summary>Returns <c>true</c> when the sentinel file exists on disk.</summary>
    public bool IsSafeMode => File.Exists(_sentinelPath);

    /// <summary>
    /// Creates the sentinel file, recording the UTC timestamp and <paramref name="reason"/>.
    /// Idempotent — safe to call when already in safe mode.
    /// </summary>
    public void EnterSafeMode(string reason)
    {
        var dir = Path.GetDirectoryName(_sentinelPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(_sentinelPath, $"{DateTimeOffset.UtcNow:O}|{reason}");
    }

    /// <summary>
    /// Deletes the sentinel file, allowing child processes to be spawned again.
    /// Idempotent — safe to call when not in safe mode.
    /// </summary>
    public void ExitSafeMode()
    {
        if (File.Exists(_sentinelPath))
            File.Delete(_sentinelPath);
    }
}
