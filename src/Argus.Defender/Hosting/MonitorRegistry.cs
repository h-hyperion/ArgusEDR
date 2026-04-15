using System.Text.Json;
using Argus.Core;
using Argus.Core.IPC;
using Microsoft.Extensions.Logging;

namespace Argus.Defender.Hosting;

/// <summary>
/// Result returned by <see cref="MonitorRegistry.ToggleAsync"/>.
/// </summary>
public enum ToggleResult
{
    /// <summary>The toggle was applied successfully and persisted to disk.</summary>
    Success,

    /// <summary>No monitor with the requested ID is registered.</summary>
    NotFound,

    /// <summary>The monitor was found but Enable/Disable threw an exception.</summary>
    Error,
}

/// <summary>
/// Central registry for all <see cref="IDefenderMonitor"/> instances.
/// Persists the enabled/disabled state to <c>MonitorConfig.json</c> atomically
/// (write-to-temp + rename) so a crash during a write never corrupts the config.
/// Thread safety for concurrent toggle requests is provided by a <see cref="SemaphoreSlim"/>.
/// </summary>
public sealed class MonitorRegistry
{
    private static readonly JsonSerializerOptions _jsonOptions =
        new() { WriteIndented = true };

    private readonly IReadOnlyList<IDefenderMonitor> _monitors;
    private readonly string _configPath;
    private readonly ILogger<MonitorRegistry> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private MonitorConfig _config = new();

    /// <param name="monitors">All <see cref="IDefenderMonitor"/> registrations from DI.</param>
    /// <param name="configPath">
    ///     Path to <c>MonitorConfig.json</c>. Defaults to
    ///     <see cref="ArgusConstants.MonitorConfigPath"/> when <see langword="null"/>.
    /// </param>
    /// <param name="logger">Logger injected from the host.</param>
    public MonitorRegistry(
        IEnumerable<IDefenderMonitor> monitors,
        string? configPath,
        ILogger<MonitorRegistry> logger)
    {
        _monitors   = monitors.ToList();
        _configPath = configPath ?? ArgusConstants.MonitorConfigPath;
        _logger     = logger;

        LoadConfig();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the persisted enabled flag for the given monitor ID.
    /// The registry is the source of truth; call this rather than reading
    /// <see cref="IDefenderMonitor.IsEnabled"/> for the configured state.
    /// </summary>
    public bool IsEnabled(string monitorId) =>
        _config.Enabled.TryGetValue(monitorId, out var v) && v;

    /// <summary>
    /// Enables or disables the monitor with the given ID, updates the in-memory config,
    /// and persists the change atomically to disk.
    /// </summary>
    public async Task<ToggleResult> ToggleAsync(
        string id,
        bool enabled,
        CancellationToken ct = default)
    {
        var monitor = _monitors.FirstOrDefault(
            m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

        if (monitor is null)
        {
            _logger.LogWarning("ToggleAsync: no monitor registered with id '{Id}'", id);
            return ToggleResult.NotFound;
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (enabled)
                await monitor.EnableAsync(ct).ConfigureAwait(false);
            else
                await monitor.DisableAsync(ct).ConfigureAwait(false);

            _config.Enabled[monitor.Id] = enabled;
            await PersistConfigAsync(ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Monitor '{Id}' toggled {State}",
                monitor.Id, enabled ? "enabled" : "disabled");

            return ToggleResult.Success;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error toggling monitor '{Id}'", id);
            return ToggleResult.Error;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Returns a snapshot of all registered monitors' current state.
    /// </summary>
    public Task<IReadOnlyList<MonitorState>> GetAllStatesAsync(
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<MonitorState> states = _monitors
            .Select(m => new MonitorState(
                m.Id,
                m.DisplayName,
                IsEnabled(m.Id),
                m.Status.ToString(),
                m.EventsEmitted))
            .ToList();

        return Task.FromResult(states);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void LoadConfig()
    {
        MonitorConfig? loaded = null;

        if (File.Exists(_configPath))
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                loaded = JsonSerializer.Deserialize<MonitorConfig>(json, _jsonOptions);
                if (loaded is not null)
                {
                    // Deserialization creates a default (ordinal) comparer — rewrap for case-insensitive lookups.
                    loaded.Enabled = new Dictionary<string, bool>(loaded.Enabled, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not deserialize MonitorConfig from '{Path}'; using defaults",
                    _configPath);
            }
        }

        var defaults = MonitorConfig.Defaults();

        if (loaded is null)
        {
            _config = defaults;
            return;
        }

        // Merge: any monitor ID missing from the file gets its default value.
        foreach (var (key, value) in defaults.Enabled)
        {
            if (!loaded.Enabled.ContainsKey(key))
                loaded.Enabled[key] = value;
        }

        _config = loaded;
    }

    private async Task PersistConfigAsync(CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = _configPath + ".tmp";

        var json = JsonSerializer.Serialize(_config, _jsonOptions);
        await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);

        // Atomic rename — overwrites any stale .tmp left by a previous crash.
        File.Move(tmp, _configPath, overwrite: true);
    }
}
