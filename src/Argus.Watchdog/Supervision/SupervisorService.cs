namespace Argus.Watchdog.Supervision;

using Argus.Core;
using Argus.Core.Supervision;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Hosted service that spawns and supervises all registered child processes.
/// Delegates per-child lifecycle management to <see cref="ManagedChildProcess"/>.
/// Activates safe mode (via <see cref="SafeModeController"/>) if any child
/// exceeds its maximum restart attempts.
/// </summary>
public sealed class SupervisorService : IHostedService
{
    private readonly IEnumerable<ChildProcessDescriptor> _descriptors;
    private readonly ManifestVerifier                    _verifier;
    private readonly SafeModeController                  _safeMode;
    private readonly ILoggerFactory                      _loggerFactory;
    private readonly ILogger<SupervisorService>          _log;
    private readonly List<ManagedChildProcess>           _children = new();

    public SupervisorService(
        IEnumerable<ChildProcessDescriptor> descriptors,
        ManifestVerifier                    verifier,
        SafeModeController                  safeMode,
        ILoggerFactory                      loggerFactory,
        ILogger<SupervisorService>          log)
    {
        _descriptors   = descriptors;
        _verifier      = verifier;
        _safeMode      = safeMode;
        _loggerFactory = loggerFactory;
        _log           = log;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_safeMode.IsSafeMode)
        {
            _log.LogWarning("Safe mode active — not spawning children");
            return;
        }

        foreach (var desc in _descriptors)
        {
            var child = new ManagedChildProcess(
                desc,
                _verifier,
                new RestartPolicy(),
                ArgusConstants.ManifestPath,
                _loggerFactory.CreateLogger<ManagedChildProcess>());

            child.StateChanged += OnChildStateChanged;
            _children.Add(child);

            _log.LogInformation("Starting supervised child: {Name} ({ExePath})",
                desc.Name, desc.ExePath);

            await child.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _log.LogInformation("Stopping {Count} supervised child(ren).", _children.Count);

        foreach (var child in _children)
        {
            await child.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await child.DisposeAsync().ConfigureAwait(false);
        }

        _children.Clear();
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnChildStateChanged(ManagedChildProcess child, ChildState state)
    {
        _log.LogInformation("Child state changed: {Child} → {State}", child, state);

        if (state == ChildState.SafeMode)
        {
            var reason = $"Child '{child}' exceeded max restart attempts";
            _safeMode.EnterSafeMode(reason);
            _log.LogCritical(
                "Safe mode activated — sentinel written. Reason: {Reason}", reason);
        }
    }
}
