using Argus.Core;
using Argus.Core.IPC;
using Serilog;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;

namespace Argus.GUI.IPC;

/// <summary>
/// Bridges the GUI to Argus.Defender via the <c>argus-defender</c> named pipe.
/// Handles connection lifecycle, monitor state retrieval, toggle commands, and
/// periodic polling (every 2 s while active).
/// </summary>
public sealed class DefenderPipeBridge : IDisposable
{
    private const int PollIntervalMs = 2000;
    private const int ConnectTimeoutMs = 3000;
    private const int MaxConnectRetries = 2; // fast fail so UI shows Disconnected quickly

    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private NamedPipeClientStream? _pipe;
    private byte[]? _hmacKey;
    private bool _disposed;

    // ── Public state ─────────────────────────────────────────────────────────

    public bool IsConnected { get; private set; }
    public IReadOnlyList<MonitorState> MonitorStates { get; private set; } = [];

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised on the calling thread whenever monitor states are refreshed.</summary>
    public event Action<IReadOnlyList<MonitorState>>? StatesUpdated;

    /// <summary>Raised when the connection is lost or unavailable.</summary>
    public event Action? Disconnected;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempt initial connection and start the 2-second poll loop.
    /// Does not throw — connection failure fires <see cref="Disconnected"/>.
    /// </summary>
    public async Task StartAsync(CancellationToken ct)
    {
        _hmacKey = LoadHmacKey();
        if (_hmacKey is null)
        {
            Log.Debug("DefenderPipeBridge: IPC key not available — cannot connect to Defender pipe");
            Disconnected?.Invoke();
            return;
        }

        await TryConnectAndRefreshAsync(ct);

        _pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pollTask = PollLoopAsync(_pollCts.Token);
    }

    public void Stop()
    {
        _pollCts?.Cancel();
        DisposePipe();
    }

    // ── Pipe helpers ──────────────────────────────────────────────────────────

    private async Task<bool> TryConnectAndRefreshAsync(CancellationToken ct)
    {
        DisposePipe();
        IsConnected = false;

        if (_hmacKey is null)
        {
            Disconnected?.Invoke();
            return false;
        }

        try
        {
            _pipe = new NamedPipeClientStream(".", ArgusConstants.DefenderPipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous);

            // Fast timeout so the GUI doesn't stall when Defender isn't running
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ConnectTimeoutMs);
            await _pipe.ConnectAsync(timeoutCts.Token);

            IsConnected = true;
            Log.Information("DefenderPipeBridge connected to {Pipe}", ArgusConstants.DefenderPipeName);

            // Initial state fetch
            await SendRequestAndUpdateStatesAsync(ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // App shutting down — silent
            return false;
        }
        catch (Exception ex)
        {
            IsConnected = false;
            Log.Debug(ex, "DefenderPipeBridge: could not connect to Defender pipe");
            Disconnected?.Invoke();
            DisposePipe();
            return false;
        }
    }

    private async Task SendRequestAndUpdateStatesAsync(CancellationToken ct)
    {
        if (_pipe is null || !_pipe.IsConnected || _hmacKey is null)
            return;

        var request = PipeMessage.Create(
            PipeMessageType.Defender_GetMonitorStates,
            ArgusConstants.ModuleGui,
            new GetMonitorStatesRequest());

        await SendAsync(request, ct);

        var response = await PipeMessage.ReadFramedAsync(_pipe, _hmacKey, ct);
        if (response?.Type == PipeMessageType.Defender_MonitorStatesResponse && response.Payload is not null)
        {
            var payload = JsonSerializer.Deserialize<MonitorStatesResponse>(response.Payload);
            if (payload is not null)
            {
                MonitorStates = payload.Monitors;
                StatesUpdated?.Invoke(MonitorStates);
            }
        }
    }

    /// <summary>
    /// Send a toggle command and return the response.
    /// Returns null on failure (pipe disconnected, timeout, etc.).
    /// </summary>
    public async Task<ToggleMonitorResponse?> ToggleMonitorAsync(
        string monitorId, bool enabled, CancellationToken ct)
    {
        if (_hmacKey is null) return null;

        // Each command uses a fresh per-request connection to keep things simple
        // (same pattern as GuiPipeBridge.SendScanRequestAsync)
        try
        {
            using var pipe = new NamedPipeClientStream(".", ArgusConstants.DefenderPipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ConnectTimeoutMs);
            await pipe.ConnectAsync(timeoutCts.Token);

            var request = PipeMessage.Create(
                PipeMessageType.Defender_ToggleMonitor,
                ArgusConstants.ModuleGui,
                new ToggleMonitorRequest(monitorId, enabled));

            var frame = request.ToFramedBytes(_hmacKey);
            await pipe.WriteAsync(frame, ct);
            await pipe.FlushAsync(ct);

            var response = await PipeMessage.ReadFramedAsync(pipe, _hmacKey, ct);
            if (response?.Type == PipeMessageType.Defender_ToggleMonitorResponse && response.Payload is not null)
                return JsonSerializer.Deserialize<ToggleMonitorResponse>(response.Payload);

            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "DefenderPipeBridge: toggle command failed for {MonitorId}", monitorId);
            return null;
        }
    }

    // ── Poll loop ─────────────────────────────────────────────────────────────

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollIntervalMs, ct);

                if (!IsConnected || _pipe is null || !_pipe.IsConnected)
                {
                    // Attempt reconnect
                    await TryConnectAndRefreshAsync(ct);
                }
                else
                {
                    await SendRequestAndUpdateStatesAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "DefenderPipeBridge: poll iteration error — will retry");
                IsConnected = false;
                DisposePipe();
                Disconnected?.Invoke();
            }
        }
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private async Task SendAsync(PipeMessage message, CancellationToken ct)
    {
        if (_pipe is null || !_pipe.IsConnected || _hmacKey is null)
            throw new InvalidOperationException("Not connected to Defender pipe");
        var frame = message.ToFramedBytes(_hmacKey);
        await _pipe.WriteAsync(frame, ct);
        await _pipe.FlushAsync(ct);
    }

    private void DisposePipe()
    {
        try { _pipe?.Dispose(); } catch { /* ignore */ }
        _pipe = null;
        IsConnected = false;
    }

    private static byte[]? LoadHmacKey()
    {
        try
        {
            var keyPath = ArgusConstants.IpcKeyPath;
            if (!File.Exists(keyPath)) return null;

            var protectedBytes = File.ReadAllBytes(keyPath);
            var raw = ProtectedData.Unprotect(
                protectedBytes, null, DataProtectionScope.LocalMachine);

            if (raw.Length != 32)
            {
                Log.Warning("DefenderPipeBridge: IPC key has wrong size ({Size} bytes)", raw.Length);
                return null;
            }
            return raw;
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (CryptographicException ex)
        {
            Log.Warning(ex, "DefenderPipeBridge: DPAPI unprotect failed");
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "DefenderPipeBridge: failed to load IPC key");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
