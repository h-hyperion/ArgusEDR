using Argus.Core;
using Argus.Core.IPC;
using Serilog;
using System.IO;
using System.Text.Json;

namespace Argus.GUI.IPC;

/// <summary>
/// Bridges the GUI to the Watchdog service via named pipe IPC.
/// Handles connection lifecycle, status polling, and Safe Mode integration.
/// </summary>
public sealed class GuiPipeBridge : IDisposable
{
    private const int PollIntervalMs = 5000;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private bool _pipeAvailable;

    public string StatusMessage { get; private set; } = "Searching for Argus service...";
    public ServiceStatus? LatestStatus { get; private set; }
    public bool PipeConnected => _pipeAvailable;

    public event Action<ServiceStatus>? StatusUpdated;
    public event Action<string>? SafeModeTriggered;

    public GuiPipeBridge()
    {
    }

    public async Task StartAsync(CancellationToken ct)
    {
        // Check sentinel first — it's a local file read, no pipe needed
        CheckSentinel();

        // Try to discover the IPC key then connect
        var hmacKey = LoadHmacKey();

        if (hmacKey == null)
        {
            StatusMessage = "Waiting for Argus service (IPC key not yet configured)";
            Log.Debug("IPC key not available — pipe will be unavailable until install");
            return;
        }

        var pipeClient = new ArgusPipeClient(hmacKey, ArgusConstants.ModuleGui);

        try
        {
            await pipeClient.ConnectAsync(ct);
            _pipeAvailable = true;
            StatusMessage = "Connected to Argus service";

            // Send initial status request
            var msg = PipeMessage.Create(
                PipeMessageType.StatusRequest,
                ArgusConstants.ModuleGui,
                new { RequestType = "FullStatus" });
            await pipeClient.SendAsync(msg, ct);

            // Read response
            var response = await pipeClient.ReceiveAsync(ct);
            if (response?.Type == PipeMessageType.StatusResponse && response.Payload != null)
            {
                var status = JsonSerializer.Deserialize<ServiceStatus>(response.Payload);
                if (status != null)
                {
                    LatestStatus = status;
                    StatusUpdated?.Invoke(status);
                    CheckSafeModeFromService(status);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _pipeAvailable = false;
            StatusMessage = "Argus service disconnected";
            Log.Warning(ex, "Failed to connect to Watchdog pipe");
        }

        // Start polling loop if connected
        if (_pipeAvailable && hmacKey != null)
        {
            _pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _pollTask = PollLoopAsync(pipeClient, hmacKey, _pollCts.Token);
        }
    }

    private static byte[]? LoadHmacKey()
    {
        try
        {
            var keyPath = ArgusConstants.IpcKeyPath;
            if (!File.Exists(keyPath))
                return null;

            // Key is stored as DPAPI-protected raw bytes (LocalMachine scope)
            var protectedBytes = File.ReadAllBytes(keyPath);
            var raw = System.Security.Cryptography.ProtectedData.Unprotect(
                protectedBytes, null,
                System.Security.Cryptography.DataProtectionScope.LocalMachine);

            if (raw.Length != 32)
            {
                Log.Warning("IPC key has wrong size ({Size} bytes)", raw.Length);
                return null;
            }
            return raw;
        }
        catch (UnauthorizedAccessException)
        {
            // Standard user can't read SYSTEM-only config — will retry after elevation
            return null;
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            Log.Warning(ex, "Failed to decrypt IPC key (DPAPI scope mismatch or corrupt key)");
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load IPC key");
            return null;
        }
    }

    private bool CheckSentinel()
    {
        try
        {
            if (File.Exists(ArgusConstants.SafeModeSentinelPath))
            {
                var lines = File.ReadAllLines(ArgusConstants.SafeModeSentinelPath);
                var reasonLines = lines.Where(l => l.StartsWith("Reason: ")).Select(l => l.Substring(8));
                SafeModeTriggered?.Invoke(string.Join(", ", reasonLines));
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read Safe Mode sentinel");
            SafeModeTriggered?.Invoke("System integrity compromised");
            return true;
        }
        return false;
    }

    private void CheckSafeModeFromService(ServiceStatus status)
    {
        if (status.SafeModeActive)
        {
            SafeModeTriggered?.Invoke(status.SafeModeReason ?? "Module integrity failure");
        }
    }

    private async Task PollLoopAsync(ArgusPipeClient pipe, byte[] hmacKey, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollIntervalMs, ct);

                var msg = PipeMessage.Create(
                    PipeMessageType.StatusRequest,
                    ArgusConstants.ModuleGui,
                    new { RequestType = "FullStatus" });
                await pipe.SendAsync(msg, ct);

                var response = await pipe.ReceiveAsync(ct);
                if (response?.Type == PipeMessageType.StatusResponse && response.Payload != null)
                {
                    var status = JsonSerializer.Deserialize<ServiceStatus>(response.Payload);
                    if (status != null)
                    {
                        LatestStatus = status;
                        StatusUpdated?.Invoke(status);

                        if (status.SafeModeActive && !CheckSentinel())
                        {
                            SafeModeTriggered?.Invoke(status.SafeModeReason ?? "Unknown");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _pipeAvailable = false;
                StatusMessage = "Argus service disconnected";
                Log.Warning(ex, "Polling loop error");
                break;
            }
        }
    }

    public async Task SendScanRequestAsync(string scanPath, CancellationToken ct)
    {
        if (!_pipeAvailable)
            throw new InvalidOperationException("Not connected to Argus service");

        var hmacKey = LoadHmacKey();
        if (hmacKey == null)
            throw new InvalidOperationException("IPC key not available");

        using var pipe = new ArgusPipeClient(hmacKey, ArgusConstants.ModuleGui);
        await pipe.ConnectAsync(ct);

        var msg = PipeMessage.Create(
            PipeMessageType.ScanRequest,
            ArgusConstants.ModuleGui,
            new { Path = scanPath });
        await pipe.SendAsync(msg, ct);
    }

    public void Dispose()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
    }
}
