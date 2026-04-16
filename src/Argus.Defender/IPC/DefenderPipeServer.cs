using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Argus.Core;
using Argus.Core.IPC;
using Argus.Defender.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Argus.Defender.IPC;

/// <summary>
/// Named-pipe server for Argus.Defender IPC.
/// Accepts connections from the GUI (and other modules) and routes:
/// <list type="bullet">
///   <item><see cref="PipeMessageType.Defender_GetMonitorStates"/> → <see cref="MonitorRegistry.GetAllStatesAsync"/></item>
///   <item><see cref="PipeMessageType.Defender_ToggleMonitor"/>    → <see cref="MonitorRegistry.ToggleAsync"/></item>
/// </list>
/// HMAC framing is byte-compatible with <c>WatchdogPipeServer</c>: the GUI uses the
/// same <see cref="ArgusPipeClient"/> framing for both pipes.
/// ACL mirrors WatchdogPipeServer: SYSTEM (full) + Administrators (read/write) + Interactive Users (read/write).
/// </summary>
public sealed class DefenderPipeServer : BackgroundService, IDisposable
{
    private readonly MonitorRegistry _registry;
    private readonly ILogger<DefenderPipeServer> _logger;
    private readonly string _pipeName;
    private readonly byte[] _hmacKey;

    // ── Constructors ─────────────────────────────────────────────────────────

    /// <summary>
    /// Production constructor: loads the HMAC key from the DPAPI-protected file at
    /// <see cref="ArgusConstants.IpcKeyPath"/>.
    /// </summary>
    public DefenderPipeServer(
        MonitorRegistry registry,
        ILogger<DefenderPipeServer> logger)
        : this(registry, logger, ArgusConstants.DefenderPipeName, LoadHmacKey())
    {
    }

    /// <summary>
    /// Testability constructor: caller supplies the pipe name and HMAC key directly,
    /// bypassing DPAPI and the file system.
    /// </summary>
    public DefenderPipeServer(
        MonitorRegistry registry,
        ILogger<DefenderPipeServer> logger,
        string pipeName,
        byte[] hmacKey)
    {
        _registry = registry;
        _logger   = logger;
        _pipeName = pipeName;
        _hmacKey  = hmacKey;
    }

    // ── BackgroundService ────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DefenderPipeServer starting on pipe '{Pipe}'", _pipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreateSecuredPipe();
                await pipe.WaitForConnectionAsync(stoppingToken);
                _logger.LogDebug("Defender pipe client connected");
                // Fire-and-forget per-client handler — mirrors WatchdogPipeServer pattern.
                _ = Task.Run(() => HandleClientAsync(pipe, stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Defender pipe accept error");
                pipe?.Dispose();
            }
        }

        _logger.LogInformation("DefenderPipeServer stopped");
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private NamedPipeServerStream CreateSecuredPipe()
    {
        var pipeSecurity = new PipeSecurity();

        // SYSTEM: full control (Defender itself, Watchdog, Recovery).
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        // Administrators: read/write (elevated GUI).
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));

        // Interactive Users: read/write (standard GUI, Scanner).
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,   // byte mode for length-prefixed framing
            PipeOptions.Asynchronous,
            inBufferSize:  65536,
            outBufferSize: 65536,
            pipeSecurity);
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            while (pipe.IsConnected && !ct.IsCancellationRequested)
            {
                var msg = await PipeMessage.ReadFramedAsync(pipe, _hmacKey, ct);
                if (msg is null) break;

                if (msg.Version != PipeMessage.CurrentProtocolVersion)
                {
                    _logger.LogWarning(
                        "Rejected message with unknown protocol v{V} from {Sender}",
                        msg.Version, msg.SenderModule);
                    continue;
                }

                var response = await DispatchAsync(msg, ct);
                if (response is not null)
                {
                    var frame = response.ToFramedBytes(_hmacKey);
                    await pipe.WriteAsync(frame, ct);
                    await pipe.FlushAsync(ct);
                }
            }
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "Defender pipe: HMAC verification failed — possible spoofing attempt");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Defender pipe client disconnected unexpectedly");
        }
        finally
        {
            pipe.Dispose();
            _logger.LogDebug("Defender pipe client disconnected");
        }
    }

    private async Task<PipeMessage?> DispatchAsync(PipeMessage msg, CancellationToken ct)
    {
        switch (msg.Type)
        {
            case PipeMessageType.Defender_GetMonitorStates:
            {
                var states = await _registry.GetAllStatesAsync(ct);
                return PipeMessage.Create(
                    PipeMessageType.Defender_MonitorStatesResponse,
                    ArgusConstants.ModuleDefender,
                    new MonitorStatesResponse(states),
                    inReplyTo: msg.CorrelationId);
            }

            case PipeMessageType.Defender_ToggleMonitor:
            {
                var req = msg.GetPayload<ToggleMonitorRequest>();
                if (req is null)
                {
                    return PipeMessage.Create(
                        PipeMessageType.Defender_ToggleMonitorResponse,
                        ArgusConstants.ModuleDefender,
                        new ToggleMonitorResponse(false, "Malformed ToggleMonitorRequest payload"),
                        inReplyTo: msg.CorrelationId);
                }

                var result = await _registry.ToggleAsync(req.MonitorId, req.Enabled, ct);

                var (success, error) = result switch
                {
                    ToggleResult.Success  => (true,  (string?)null),
                    ToggleResult.NotFound => (false, $"No monitor registered with id '{req.MonitorId}'"),
                    ToggleResult.Error    => (false, $"Monitor '{req.MonitorId}' threw an exception during toggle"),
                    _                    => (false, "Unknown toggle result")
                };

                return PipeMessage.Create(
                    PipeMessageType.Defender_ToggleMonitorResponse,
                    ArgusConstants.ModuleDefender,
                    new ToggleMonitorResponse(success, error),
                    inReplyTo: msg.CorrelationId);
            }

            default:
                _logger.LogWarning(
                    "DefenderPipeServer: unrecognised message type {Type} from {Sender}",
                    msg.Type, msg.SenderModule);
                return null;
        }
    }

    // ── DPAPI key load ───────────────────────────────────────────────────────

    /// <summary>
    /// Loads the HMAC key from the DPAPI-protected file at <see cref="ArgusConstants.IpcKeyPath"/>.
    /// Mirrors the pattern used by WatchdogPipeServer.
    /// </summary>
    private static byte[] LoadHmacKey()
    {
        var protected_ = File.ReadAllBytes(ArgusConstants.IpcKeyPath);
        return ProtectedData.Unprotect(protected_, null, DataProtectionScope.LocalMachine);
    }
}
