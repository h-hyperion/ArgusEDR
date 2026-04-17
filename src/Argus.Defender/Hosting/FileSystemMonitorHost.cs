using Argus.Defender.Monitors;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Argus.Defender.Hosting;

/// <summary>
/// <see cref="IDefenderMonitor"/> / <see cref="BackgroundService"/> wrapper for
/// <see cref="FileSystemMonitor"/>. The inner watcher is created on <see cref="EnableAsync"/>
/// and torn down on <see cref="DisableAsync"/>.
///
/// EventsEmitted: incremented on each <c>FileChanged</c> callback from the inner monitor.
/// </summary>
public sealed class FileSystemMonitorHost : BackgroundService, IDefenderMonitor
{
    // ── IDefenderMonitor identity ────────────────────────────────────────────
    public string Id          => "filesystem";
    public string DisplayName => "File System Monitor";

    // ── IDefenderMonitor state ───────────────────────────────────────────────
    public bool          IsEnabled     { get; private set; }
    public MonitorStatus Status        { get; private set; } = MonitorStatus.Stopped;
    public long          EventsEmitted => Interlocked.Read(ref _eventsEmitted);

    // ── Internals ────────────────────────────────────────────────────────────
    private long                    _eventsEmitted;
    private FileSystemMonitor?      _inner;
    private CancellationTokenSource? _innerCts;
    private Task?                   _innerTask;

    private readonly IEnumerable<string>             _watchedPaths;
    private readonly ILogger<FileSystemMonitorHost>  _log;

    public FileSystemMonitorHost(
        IEnumerable<string>            watchedPaths,
        ILogger<FileSystemMonitorHost> log)
    {
        _watchedPaths = watchedPaths;
        _log          = log;
    }

    // ── BackgroundService ────────────────────────────────────────────────────

    /// <summary>
    /// The registry controls enable/disable. ExecuteAsync just parks until shutdown.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { /* normal shutdown */ }
        finally { await DisableAsync(CancellationToken.None).ConfigureAwait(false); }
    }

    // ── IDefenderMonitor control ─────────────────────────────────────────────

    public async Task EnableAsync(CancellationToken ct)
    {
        if (IsEnabled) return;

        try
        {
            _innerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _inner    = new FileSystemMonitor(_watchedPaths);

            // Count every file event via the FileChanged event.
            _inner.FileChanged += OnFileChanged;

            // StartAsync blocks until the CancellationToken fires; run it in the background.
            _innerTask = _inner.StartAsync(_innerCts.Token);

            IsEnabled = true;
            Status    = MonitorStatus.Running;
            _log.LogInformation("{Id} enabled — watching {Count} path(s)", Id, _watchedPaths.Count());
        }
        catch (Exception ex)
        {
            Status = MonitorStatus.Error;
            _log.LogError(ex, "Failed to enable {Id}", Id);
            throw;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task DisableAsync(CancellationToken ct)
    {
        if (!IsEnabled) return;

        try
        {
            _innerCts?.Cancel();

            if (_innerTask is not null)
            {
                try { await _innerTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { /* expected */ }
                _innerTask = null;
            }

            if (_inner is not null)
            {
                _inner.FileChanged -= OnFileChanged;
                _inner.Dispose();
                _inner = null;
            }

            _innerCts?.Dispose();
            _innerCts = null;

            IsEnabled = false;
            Status    = MonitorStatus.Stopped;
            _log.LogInformation("{Id} disabled", Id);
        }
        catch (Exception ex)
        {
            Status = MonitorStatus.Error;
            _log.LogError(ex, "Failed to disable {Id}", Id);
            throw;
        }
    }

    // ── Event handler ────────────────────────────────────────────────────────

    private void OnFileChanged(object? sender, string path)
        => Interlocked.Increment(ref _eventsEmitted);

    // ── Dispose ──────────────────────────────────────────────────────────────

    public override void Dispose()
    {
        _innerCts?.Dispose();
        _inner?.Dispose();
        base.Dispose();
    }
}
