using System.Text.Json;
using Argus.Core.IPC;
using Argus.Defender.Hosting;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Argus.Defender.Tests.Hosting;

/// <summary>
/// Tests for <see cref="MonitorRegistry"/>.
/// Each test gets its own temp directory so config file I/O is isolated.
/// </summary>
public sealed class MonitorRegistryTests : IDisposable
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private readonly string _tempDir;

    public MonitorRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private string ConfigPath => Path.Combine(_tempDir, "MonitorConfig.json");

    private MonitorRegistry BuildRegistry(IEnumerable<IDefenderMonitor>? monitors = null) =>
        new(
            monitors ?? [],
            ConfigPath,
            NullLogger<MonitorRegistry>.Instance);

    private static Mock<IDefenderMonitor> MakeMock(
        string id,
        string displayName = "Test Monitor",
        bool isEnabled = false,
        MonitorStatus status = MonitorStatus.Stopped,
        long events = 0)
    {
        var mock = new Mock<IDefenderMonitor>();
        mock.Setup(m => m.Id).Returns(id);
        mock.Setup(m => m.DisplayName).Returns(displayName);
        mock.Setup(m => m.IsEnabled).Returns(isEnabled);
        mock.Setup(m => m.Status).Returns(status);
        mock.Setup(m => m.EventsEmitted).Returns(events);
        mock.Setup(m => m.EnableAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(m => m.DisableAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return mock;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>Test 1: empty monitor set → GetAllStatesAsync returns empty list.</summary>
    [Fact]
    public async Task GetAllStatesAsync_NoMonitors_ReturnsEmpty()
    {
        var registry = BuildRegistry([]);

        var states = await registry.GetAllStatesAsync();

        states.Should().BeEmpty();
    }

    /// <summary>Test 2: registry reflects states of all registered monitors.</summary>
    [Fact]
    public async Task GetAllStatesAsync_WithMonitors_ReturnsOneStatePerMonitor()
    {
        var fs  = MakeMock("filesystem",  "File System Monitor",  isEnabled: false, status: MonitorStatus.Stopped, events: 0);
        var etw = MakeMock("etw",         "ETW Monitor",          isEnabled: true,  status: MonitorStatus.Running, events: 42);

        // Seed config so 'filesystem' and 'etw' are explicitly enabled/disabled.
        var seedConfig = new MonitorConfig
        {
            Enabled = new() { ["filesystem"] = false, ["etw"] = true }
        };
        await File.WriteAllTextAsync(ConfigPath,
            JsonSerializer.Serialize(seedConfig, new JsonSerializerOptions { WriteIndented = true }));

        var registry = BuildRegistry([fs.Object, etw.Object]);

        var states = await registry.GetAllStatesAsync();

        states.Should().HaveCount(2);

        var fsState = states.Single(s => s.Id == "filesystem");
        fsState.Enabled.Should().BeFalse();
        fsState.Status.Should().Be("Stopped");
        fsState.EventsEmitted.Should().Be(0);

        var etwState = states.Single(s => s.Id == "etw");
        etwState.Enabled.Should().BeTrue();
        etwState.Status.Should().Be("Running");
        etwState.EventsEmitted.Should().Be(42);
    }

    /// <summary>
    /// Test 3a: when MonitorConfig.json exists, enabled flags come from the file.
    /// </summary>
    [Fact]
    public async Task LoadConfig_WhenFileExists_UsesPersistedFlags()
    {
        var config = new MonitorConfig
        {
            Enabled = new() { ["filesystem"] = false, ["etw"] = false }
        };
        await File.WriteAllTextAsync(ConfigPath,
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        var registry = BuildRegistry([MakeMock("filesystem").Object, MakeMock("etw").Object]);

        registry.IsEnabled("filesystem").Should().BeFalse();
        registry.IsEnabled("etw").Should().BeFalse();
    }

    /// <summary>
    /// Test 3b: when no config file exists, per-monitor defaults from MonitorConfig.Defaults() are used.
    /// </summary>
    [Fact]
    public void LoadConfig_WhenNoFileExists_UsesDefaults()
    {
        File.Exists(ConfigPath).Should().BeFalse("pre-condition: no config file");

        var registry = BuildRegistry([MakeMock("filesystem").Object, MakeMock("byovd").Object]);

        registry.IsEnabled("filesystem").Should().BeTrue(  "filesystem is enabled by default");
        registry.IsEnabled("byovd").Should().BeFalse("byovd is disabled by default");
    }

    /// <summary>
    /// Test 3c: IDs present in the monitor set but absent from the file are merged in with defaults.
    /// </summary>
    [Fact]
    public async Task LoadConfig_MissingIdsInFile_MergedFromDefaults()
    {
        // File only has 'etw'; 'filesystem' is missing → should get its default (true).
        var partial = new MonitorConfig { Enabled = new() { ["etw"] = false } };
        await File.WriteAllTextAsync(ConfigPath,
            JsonSerializer.Serialize(partial, new JsonSerializerOptions { WriteIndented = true }));

        var registry = BuildRegistry([MakeMock("filesystem").Object, MakeMock("etw").Object]);

        registry.IsEnabled("filesystem").Should().BeTrue("merged from defaults");
        registry.IsEnabled("etw").Should().BeFalse("overridden by file");
    }

    /// <summary>
    /// Test 4: ToggleAsync disables a monitor, calls DisableAsync on it, and writes config to disk.
    /// </summary>
    [Fact]
    public async Task ToggleAsync_Disable_CallsDisableAsyncAndPersistsConfig()
    {
        var monitor = MakeMock("filesystem", isEnabled: true);
        var registry = BuildRegistry([monitor.Object]);

        var result = await registry.ToggleAsync("filesystem", enabled: false);

        result.Should().Be(ToggleResult.Success);
        monitor.Verify(m => m.DisableAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Config must be on disk now.
        File.Exists(ConfigPath).Should().BeTrue();
        var written = JsonSerializer.Deserialize<MonitorConfig>(
            await File.ReadAllTextAsync(ConfigPath))!;
        written.Enabled["filesystem"].Should().BeFalse();
    }

    /// <summary>
    /// Test 4 (enable path): ToggleAsync enables a monitor and persists the change.
    /// </summary>
    [Fact]
    public async Task ToggleAsync_Enable_CallsEnableAsyncAndPersistsConfig()
    {
        var monitor = MakeMock("filesystem", isEnabled: false);
        var registry = BuildRegistry([monitor.Object]);

        var result = await registry.ToggleAsync("filesystem", enabled: true);

        result.Should().Be(ToggleResult.Success);
        monitor.Verify(m => m.EnableAsync(It.IsAny<CancellationToken>()), Times.Once);

        var written = JsonSerializer.Deserialize<MonitorConfig>(
            await File.ReadAllTextAsync(ConfigPath))!;
        written.Enabled["filesystem"].Should().BeTrue();
    }

    /// <summary>Test 5: ToggleAsync with an unknown ID returns NotFound.</summary>
    [Fact]
    public async Task ToggleAsync_UnknownId_ReturnsNotFound()
    {
        var registry = BuildRegistry([MakeMock("filesystem").Object]);

        var result = await registry.ToggleAsync("unknown-id", enabled: true);

        result.Should().Be(ToggleResult.NotFound);
    }

    /// <summary>
    /// Test 5b: ToggleAsync is case-insensitive on monitor IDs.
    /// </summary>
    [Fact]
    public async Task ToggleAsync_CaseInsensitiveId_FindsMonitor()
    {
        var monitor = MakeMock("filesystem");
        var registry = BuildRegistry([monitor.Object]);

        var result = await registry.ToggleAsync("FileSystem", enabled: false);

        result.Should().Be(ToggleResult.Success);
    }

    /// <summary>
    /// Test 6: after a successful ToggleAsync, no orphaned .tmp file is left on disk.
    /// </summary>
    [Fact]
    public async Task ToggleAsync_Success_LeavesNoTmpFile()
    {
        var monitor = MakeMock("filesystem");
        var registry = BuildRegistry([monitor.Object]);

        await registry.ToggleAsync("filesystem", enabled: false);

        var tmpPath = ConfigPath + ".tmp";
        File.Exists(tmpPath).Should().BeFalse("temp file must be renamed to final path");
        File.Exists(ConfigPath).Should().BeTrue("final config file must exist");
    }
}
