using Argus.Defender.Hosting;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Argus.Defender.Tests.Hosting;

/// <summary>
/// Tests for <see cref="FileSystemMonitorHost"/>.
/// Uses a real temporary directory so the underlying FileSystemWatcher has a valid path.
/// </summary>
public sealed class FileSystemMonitorHostTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemMonitorHostTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"argus-fsm-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private FileSystemMonitorHost BuildHost() =>
        new(new[] { _tempDir }, NullLogger<FileSystemMonitorHost>.Instance);

    // ── Test 1: identity ────────────────────────────────────────────────────

    [Fact]
    public void Id_IsFilesystem()
    {
        using var host = BuildHost();
        host.Id.Should().Be("filesystem");
    }

    [Fact]
    public void DisplayName_IsNonEmpty()
    {
        using var host = BuildHost();
        host.DisplayName.Should().NotBeNullOrWhiteSpace();
    }

    // ── Test 2: freshly constructed state ───────────────────────────────────

    [Fact]
    public void FreshHost_IsDisabled()
    {
        using var host = BuildHost();
        host.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void FreshHost_StatusIsStopped()
    {
        using var host = BuildHost();
        host.Status.Should().Be(MonitorStatus.Stopped);
    }

    [Fact]
    public void FreshHost_EventsEmittedIsZero()
    {
        using var host = BuildHost();
        host.EventsEmitted.Should().Be(0);
    }

    // ── Test 3: after EnableAsync ────────────────────────────────────────────

    [Fact]
    public async Task AfterEnable_IsEnabledAndRunning()
    {
        using var host = BuildHost();
        await host.EnableAsync(CancellationToken.None);

        host.IsEnabled.Should().BeTrue();
        host.Status.Should().Be(MonitorStatus.Running);

        // Cleanup — disable to cancel the inner watcher task.
        await host.DisableAsync(CancellationToken.None);
    }

    // ── Test 4: enable then disable ─────────────────────────────────────────

    [Fact]
    public async Task AfterEnableThenDisable_IsDisabledAndStopped()
    {
        using var host = BuildHost();
        await host.EnableAsync(CancellationToken.None);
        await host.DisableAsync(CancellationToken.None);

        host.IsEnabled.Should().BeFalse();
        host.Status.Should().Be(MonitorStatus.Stopped);
    }

    // ── Test 5: idempotent enable ────────────────────────────────────────────

    [Fact]
    public async Task EnableAsync_CalledTwice_IsIdempotent()
    {
        using var host = BuildHost();
        await host.EnableAsync(CancellationToken.None);
        await host.EnableAsync(CancellationToken.None); // should not throw or double-start

        host.IsEnabled.Should().BeTrue();
        host.Status.Should().Be(MonitorStatus.Running);

        await host.DisableAsync(CancellationToken.None);
    }
}
