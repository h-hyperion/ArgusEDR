using Argus.Core;
using Argus.Core.IPC;
using Argus.Core.Models;
using Argus.Watchdog.IPC;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Argus.Watchdog;

public sealed class WatchdogService : BackgroundService
{
    private readonly ILogger<WatchdogService> _log;
    private readonly WatchdogPipeServer _pipe;
    private readonly byte[] _hmacKey;
    private readonly Dictionary<string, DateTimeOffset> _lastHeartbeat = new();

    public WatchdogService(ILogger<WatchdogService> log, WatchdogPipeServer pipe, byte[] hmacKey)
    {
        _log = log;
        _pipe = pipe;
        _hmacKey = hmacKey;
        _pipe.MessageReceived += OnMessageReceived;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await _pipe.StartAsync(ct);
        _log.LogInformation("Watchdog started - heartbeat interval {Interval}s, timeout {Timeout}s",
            ArgusConstants.HeartbeatInterval.TotalSeconds,
            ArgusConstants.HeartbeatTimeout.TotalSeconds);

        while (!ct.IsCancellationRequested)
        {
            CheckHeartbeats();
            await Task.Delay(ArgusConstants.HeartbeatInterval, ct);
        }
    }

    private void OnMessageReceived(object? sender, (PipeMessage Message, System.IO.Pipes.NamedPipeServerStream Pipe) args)
    {
        var msg = args.Message;
        var pipe = args.Pipe;

        if (msg.Version != PipeMessage.CurrentProtocolVersion)
        {
            _log.LogWarning("Rejected message with unknown protocol version {V} from {Sender}",
                msg.Version, msg.SenderModule);
            return;
        }

        switch (msg.Type)
        {
            case PipeMessageType.Heartbeat:
                _lastHeartbeat[msg.SenderModule] = DateTimeOffset.UtcNow;
                break;

            case PipeMessageType.StatusRequest:
                _ = Task.Run(() => HandleStatusRequestAsync(pipe, msg, CancellationToken.None));
                break;

            case PipeMessageType.ModuleError:
                var error = msg.GetPayload<ModuleError>();
                if (error?.Severity == ErrorSeverity.Fatal)
                {
                    _log.LogCritical("Fatal error from {Module}: {Message}",
                        error.ModuleId, error.Message);
                    ActivateSafeMode(error.ModuleId);
                }
                else
                {
                    _log.LogWarning("Module error from {Module}: {Message}",
                        error?.ModuleId, error?.Message);
                }
                break;

            case PipeMessageType.ThreatAlert:
                _log.LogWarning("Threat alert from {Sender}: {Payload}",
                    msg.SenderModule, msg.Payload);
                break;

            default:
                _log.LogDebug("Received {Type} from {Sender}",
                    msg.Type, msg.SenderModule);
                break;
        }
    }

    private async Task HandleStatusRequestAsync(System.IO.Pipes.NamedPipeServerStream pipe, PipeMessage request, CancellationToken ct)
    {
        try
        {
            var safeMode = ReadSafeModeSentinel();
            var isDefenderAlive = _lastHeartbeat.TryGetValue(ArgusConstants.ModuleDefender, out var last)
                && DateTimeOffset.UtcNow - last < ArgusConstants.HeartbeatTimeout;

            var status = new ServiceStatus(
                ServiceRunning: true,
                DefenderActive: isDefenderAlive,
                ThreatsDetected: 0,
                FilesScanned: 0,
                QuarantinedItems: 0,
                LastScanTime: null,
                SafeModeActive: safeMode.IsActive,
                SafeModeReason: safeMode.Reason,
                WatchdogStatus: "OK",
                DefenderStatus: isDefenderAlive ? "Active" : "Inactive");

            var response = PipeMessage.Create(
                PipeMessageType.StatusResponse,
                ArgusConstants.ModuleWatchdog,
                status,
                inReplyTo: request.CorrelationId);

            var frame = response.ToFramedBytes(_hmacKey);
            await pipe.WriteAsync(frame, ct);
            await pipe.FlushAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Failed to send status response");
        }
    }

    private static (bool IsActive, string? Reason) ReadSafeModeSentinel()
    {
        try
        {
            if (!File.Exists(ArgusConstants.SafeModeSentinelPath))
                return (false, null);

            var lines = File.ReadAllLines(ArgusConstants.SafeModeSentinelPath);
            var reasonLines = lines.Where(l => l.StartsWith("Reason: ")).Select(l => l[8..]);
            return (true, string.Join(", ", reasonLines));
        }
        catch
        {
            return (false, null);
        }
    }

    private void CheckHeartbeats()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (module, last) in _lastHeartbeat)
        {
            if (now - last > ArgusConstants.HeartbeatTimeout)
            {
                _log.LogCritical("Module {Module} heartbeat timeout - activating Safe Mode", module);
                ActivateSafeMode(module);
            }
        }
    }

    private void ActivateSafeMode(string offendingModule)
    {
        var reason = $"Module failure: {offendingModule}";
        _log.LogCritical("Activating Safe Mode - {Reason}", reason);

        try
        {
            var controller = new Argus.Recovery.SafeModeController();
            controller.Activate(reason);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to activate Safe Mode via SafeModeController - writing sentinel directly");
            try
            {
                Directory.CreateDirectory(ArgusConstants.StateDir);
                File.WriteAllText(ArgusConstants.SafeModeSentinelPath,
                    $"Activated: {DateTimeOffset.UtcNow:O}\nReason: {reason}\nMachine: {Environment.MachineName}\n");
            }
            catch (Exception innerEx)
            {
                _log.LogError(innerEx, "Failed to write Safe Mode sentinel file");
            }
        }
    }
}
