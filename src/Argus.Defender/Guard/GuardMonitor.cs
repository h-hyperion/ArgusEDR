using Argus.Defender.Monitors;
using Serilog;

namespace Argus.Defender.Guard;

public sealed class GuardMonitor
{
    private readonly GuardEnforcer _enforcer;
    private readonly GuardConfig _config;
    private readonly HashSet<string> _protectedKeys;

    public GuardMonitor(GuardEnforcer enforcer, GuardConfig config)
    {
        _enforcer = enforcer;
        _config = config;
        _protectedKeys = config.Toggles
            .Where(t => t.Enabled)
            .SelectMany(t => GuardEnforcer.GetRegistryKeysForToggle(t.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public void OnRegistryChange(MonitorEvent evt)
    {
        if (evt.RegistryKey is null) return;
        if (!_protectedKeys.Any(k => evt.RegistryKey.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return;

        Log.Warning("Privacy Guard key tampered: {Key} by PID {Pid} - re-enforcing",
            evt.RegistryKey, evt.ProcessId);
        _enforcer.ApplyAll(_config);
    }

    public int ProtectedKeyCount => _protectedKeys.Count;
}
