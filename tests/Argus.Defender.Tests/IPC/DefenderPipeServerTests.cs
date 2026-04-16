using System.IO.Pipes;
using System.Security.Cryptography;
using Argus.Core.IPC;
using Argus.Defender.Hosting;
using Argus.Defender.IPC;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Argus.Defender.Tests.IPC;

/// <summary>
/// Round-trip tests for <see cref="DefenderPipeServer"/>.
/// Each test uses a unique random pipe name so tests never collide.
/// HMAC key is randomly generated per test — no DPAPI file load needed.
/// </summary>
public sealed class DefenderPipeServerTests : IAsyncDisposable
{
    private readonly string _pipeName = $"argus-defender-test-{Guid.NewGuid():N}";
    private readonly byte[] _hmacKey;
    private readonly MonitorRegistry _registry;
    private readonly DefenderPipeServer _server;
    private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(10));

    public DefenderPipeServerTests()
    {
        // Random 32-byte key — bypasses DPAPI for tests.
        _hmacKey = RandomNumberGenerator.GetBytes(32);

        // Build a real MonitorRegistry with two fake monitors.
        var fsMonitor  = MakeMonitor("filesystem",  "File System Monitor", MonitorStatus.Running);
        var etwMonitor = MakeMonitor("etw",          "ETW Monitor",         MonitorStatus.Stopped);

        _registry = new MonitorRegistry(
            [fsMonitor.Object, etwMonitor.Object],
            configPath: null,   // no config file — uses defaults
            NullLogger<MonitorRegistry>.Instance);

        _server = new DefenderPipeServer(
            _registry,
            NullLogger<DefenderPipeServer>.Instance,
            pipeName: _pipeName,
            hmacKey:  _hmacKey);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await _server.StopAsync(CancellationToken.None);
        _server.Dispose();
        _cts.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Mock<IDefenderMonitor> MakeMonitor(
        string id, string displayName, MonitorStatus status)
    {
        var mock = new Mock<IDefenderMonitor>();
        mock.Setup(m => m.Id).Returns(id);
        mock.Setup(m => m.DisplayName).Returns(displayName);
        mock.Setup(m => m.IsEnabled).Returns(false);
        mock.Setup(m => m.Status).Returns(status);
        mock.Setup(m => m.EventsEmitted).Returns(0);
        mock.Setup(m => m.EnableAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(m => m.DisableAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return mock;
    }

    /// <summary>
    /// Connect a raw named-pipe client, send a request, receive the response.
    /// </summary>
    private async Task<PipeMessage> SendAndReceiveAsync(PipeMessage request)
    {
        using var client = new NamedPipeClientStream(".", _pipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous);

        await client.ConnectAsync(TimeSpan.FromSeconds(5), _cts.Token);

        var frame = request.ToFramedBytes(_hmacKey);
        await client.WriteAsync(frame, _cts.Token);
        await client.FlushAsync(_cts.Token);

        var response = await PipeMessage.ReadFramedAsync(client, _hmacKey, _cts.Token);
        return response!;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Test 1: GetMonitorStatesRequest → MonitorStatesResponse with all monitors.
    /// </summary>
    [Fact]
    public async Task GetMonitorStates_ReturnsAllMonitorStates()
    {
        await _server.StartAsync(_cts.Token);

        var request = PipeMessage.Create(
            PipeMessageType.Defender_GetMonitorStates,
            "GUI",
            new GetMonitorStatesRequest());

        var response = await SendAndReceiveAsync(request);

        response.Type.Should().Be(PipeMessageType.Defender_MonitorStatesResponse);
        response.InReplyTo.Should().Be(request.CorrelationId);

        var payload = response.GetPayload<MonitorStatesResponse>();
        payload.Should().NotBeNull();
        payload!.Monitors.Should().HaveCount(2);
        payload.Monitors.Select(m => m.Id).Should().Contain(["filesystem", "etw"]);
    }

    /// <summary>
    /// Test 2: ToggleMonitorRequest for a known monitor → success response.
    /// </summary>
    [Fact]
    public async Task ToggleMonitor_KnownMonitor_ReturnsSuccess()
    {
        await _server.StartAsync(_cts.Token);

        var request = PipeMessage.Create(
            PipeMessageType.Defender_ToggleMonitor,
            "GUI",
            new ToggleMonitorRequest("filesystem", false));

        var response = await SendAndReceiveAsync(request);

        response.Type.Should().Be(PipeMessageType.Defender_ToggleMonitorResponse);
        response.InReplyTo.Should().Be(request.CorrelationId);

        var payload = response.GetPayload<ToggleMonitorResponse>();
        payload.Should().NotBeNull();
        payload!.Success.Should().BeTrue();
        payload.Error.Should().BeNull();
    }

    /// <summary>
    /// Test 3: ToggleMonitorRequest for a nonexistent monitor → error response.
    /// </summary>
    [Fact]
    public async Task ToggleMonitor_UnknownMonitor_ReturnsError()
    {
        await _server.StartAsync(_cts.Token);

        var request = PipeMessage.Create(
            PipeMessageType.Defender_ToggleMonitor,
            "GUI",
            new ToggleMonitorRequest("nonexistent", true));

        var response = await SendAndReceiveAsync(request);

        response.Type.Should().Be(PipeMessageType.Defender_ToggleMonitorResponse);
        response.InReplyTo.Should().Be(request.CorrelationId);

        var payload = response.GetPayload<ToggleMonitorResponse>();
        payload.Should().NotBeNull();
        payload!.Success.Should().BeFalse();
        payload.Error.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Test 4: A message with a tampered HMAC is silently dropped (no crash, no response).
    /// The connection closes without returning a valid framed response.
    /// </summary>
    [Fact]
    public async Task TamperedHmac_ConnectionClosedGracefully()
    {
        await _server.StartAsync(_cts.Token);

        using var client = new NamedPipeClientStream(".", _pipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(TimeSpan.FromSeconds(5), _cts.Token);

        // Build a valid frame then corrupt the last byte of the HMAC.
        var request = PipeMessage.Create(
            PipeMessageType.Defender_GetMonitorStates,
            "GUI",
            new GetMonitorStatesRequest());
        var frame = request.ToFramedBytes(_hmacKey);
        frame[^1] ^= 0xFF; // flip all bits in the last HMAC byte

        await client.WriteAsync(frame, _cts.Token);
        await client.FlushAsync(_cts.Token);

        // Server should close the pipe after the HMAC failure.
        // Reading should return 0 bytes (EOF) or throw — either is acceptable.
        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var buf = new byte[4];
        int bytesRead = 0;
        try
        {
            bytesRead = await client.ReadAsync(buf, readCts.Token);
        }
        catch (OperationCanceledException) { /* timeout = server never replied = pass */ }

        bytesRead.Should().Be(0, "server should close the pipe after HMAC failure, sending no data");
    }
}
