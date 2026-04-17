using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Argus.Core.IPC;

public enum PipeMessageType
{
    Heartbeat, StatusRequest, StatusResponse,
    ThreatAlert, ScanRequest, ScanResult,
    SafeModeActivate, SafeModeDeactivate,
    GuardApply, GuardStatus,
    ModuleError,                             // Unified error reporting

    // Defender IPC (Task 6)
    Defender_GetMonitorStates,
    Defender_ToggleMonitor,
    Defender_MonitorStatesResponse,
    Defender_ToggleMonitorResponse,
}

public sealed class PipeMessage
{
    public const int CurrentProtocolVersion = 1;
    private const int MaxPayloadBytes = 1_048_576; // 1 MB

    public int Version { get; init; } = CurrentProtocolVersion;
    public PipeMessageType Type { get; init; }
    public string SenderModule { get; init; } = "";  // "Watchdog", "Defender", "Scanner", "GUI"
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public Guid? InReplyTo { get; init; }            // For request-response pairing
    public string? Payload { get; init; }
    public DateTimeOffset SentAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string? HmacSignature { get; set; }

    public T? GetPayload<T>() where T : class =>
        Payload is null ? null : JsonSerializer.Deserialize<T>(Payload);

    public static PipeMessage Create<T>(PipeMessageType type, string sender, T payload, Guid? inReplyTo = null) =>
        new()
        {
            Type = type,
            SenderModule = sender,
            Payload = JsonSerializer.Serialize(payload),
            InReplyTo = inReplyTo
        };

    public static PipeMessage Heartbeat(string sender) =>
        new() { Type = PipeMessageType.Heartbeat, SenderModule = sender };

    /// <summary>
    /// Serialize with length-prefix framing: [4-byte big-endian length][JSON body][32-byte HMAC]
    /// </summary>
    public byte[] ToFramedBytes(byte[] hmacKey)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(this);
        if (json.Length > MaxPayloadBytes)
            throw new InvalidOperationException($"Message exceeds {MaxPayloadBytes} byte limit");

        using var hmac = new HMACSHA256(hmacKey);
        var signature = hmac.ComputeHash(json);

        var totalLength = json.Length + signature.Length;
        var frame = new byte[4 + totalLength];
        BitConverter.TryWriteBytes(frame.AsSpan(0, 4),
            System.Net.IPAddress.HostToNetworkOrder(totalLength));
        json.CopyTo(frame, 4);
        signature.CopyTo(frame, 4 + json.Length);
        return frame;
    }

    /// <summary>
    /// Read a length-prefixed framed message and verify HMAC.
    /// Throws CryptographicException on HMAC mismatch (possible spoofing).
    /// </summary>
    public static async Task<PipeMessage?> ReadFramedAsync(
        Stream stream, byte[] hmacKey, CancellationToken ct)
    {
        var lengthBuf = new byte[4];
        await stream.ReadExactlyAsync(lengthBuf, ct);
        var totalLength = System.Net.IPAddress.NetworkToHostOrder(
            BitConverter.ToInt32(lengthBuf));

        if (totalLength <= 32 || totalLength > MaxPayloadBytes + 32)
            throw new InvalidOperationException($"Invalid frame length: {totalLength}");

        var frameBuf = new byte[totalLength];
        await stream.ReadExactlyAsync(frameBuf, ct);

        var jsonLength = totalLength - 32;
        var jsonBytes = frameBuf[..jsonLength];
        var receivedSig = frameBuf[jsonLength..];

        using var hmac = new HMACSHA256(hmacKey);
        var expectedSig = hmac.ComputeHash(jsonBytes);
        if (!CryptographicOperations.FixedTimeEquals(receivedSig, expectedSig))
            throw new CryptographicException("HMAC verification failed — message may be spoofed");

        return JsonSerializer.Deserialize<PipeMessage>(jsonBytes);
    }
}
