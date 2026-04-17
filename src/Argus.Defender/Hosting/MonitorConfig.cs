namespace Argus.Defender.Hosting;

/// <summary>
/// Persisted per-monitor enabled/disabled flags.
/// Serialized to <c>C:\ProgramData\Argus\Config\MonitorConfig.json</c>.
/// </summary>
public sealed class MonitorConfig
{
    /// <summary>Map of monitor ID → enabled flag. Case-insensitive.</summary>
    public Dictionary<string, bool> Enabled { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns a <see cref="MonitorConfig"/> pre-populated with the recommended defaults
    /// for a fresh Argus installation.
    /// </summary>
    public static MonitorConfig Defaults() => new()
    {
        Enabled = new(StringComparer.OrdinalIgnoreCase)
        {
            ["filesystem"]              = true,
            ["etw"]                     = true,
            ["amsi"]                    = true,
            ["dns"]                     = true,
            ["guard"]                   = true,
            ["byovd"]                   = false,
            ["quarantine-maintenance"]  = true,
        }
    };
}
