namespace Argus.Watchdog.Tests.Supervision;

using System.Diagnostics;
using System.Runtime.InteropServices;
using Argus.Core.Supervision;
using Argus.Watchdog.Supervision;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Integration tests for <see cref="SupervisorService"/> and
/// <see cref="SafeModeController"/>.
///
/// The process-spawn tests require the StubChild executable to be built first.
/// They are skipped automatically on non-Windows hosts (the test project is
/// net8.0-windows but this guard makes intent explicit).
/// </summary>
public sealed class SupervisorIntegrationTests : IDisposable
{
    // ── SafeModeController unit tests ─────────────────────────────────────────

    [Fact]
    public void SafeMode_Initially_False()
    {
        var dir        = CreateTempDir();
        var controller = new SafeModeController(Path.Combine(dir, "argus.safemode"));

        controller.IsSafeMode.Should().BeFalse();
    }

    [Fact]
    public void EnterSafeMode_Creates_SentinelFile()
    {
        var dir        = CreateTempDir();
        var sentinel   = Path.Combine(dir, "argus.safemode");
        var controller = new SafeModeController(sentinel);

        controller.EnterSafeMode("test reason");

        File.Exists(sentinel).Should().BeTrue();
        File.ReadAllText(sentinel).Should().Contain("test reason");
    }

    [Fact]
    public void ExitSafeMode_Removes_SentinelFile()
    {
        var dir        = CreateTempDir();
        var sentinel   = Path.Combine(dir, "argus.safemode");
        var controller = new SafeModeController(sentinel);

        controller.EnterSafeMode("test reason");
        controller.ExitSafeMode();

        controller.IsSafeMode.Should().BeFalse();
        File.Exists(sentinel).Should().BeFalse();
    }

    [Fact]
    public void ExitSafeMode_Is_Idempotent_When_Not_In_SafeMode()
    {
        var dir        = CreateTempDir();
        var controller = new SafeModeController(Path.Combine(dir, "argus.safemode"));

        var act = () => controller.ExitSafeMode();
        act.Should().NotThrow();
    }

    [Fact]
    public void EnterSafeMode_Creates_Parent_Directory_If_Missing()
    {
        var dir        = CreateTempDir();
        var sentinel   = Path.Combine(dir, "nested", "deep", "argus.safemode");
        var controller = new SafeModeController(sentinel);

        controller.EnterSafeMode("nested dir test");

        File.Exists(sentinel).Should().BeTrue();
    }

    // ── SupervisorService safe-mode-active test (no real processes) ───────────

    [Fact]
    public async Task StartAsync_DoesNothing_When_SafeMode_Active()
    {
        var dir        = CreateTempDir();
        var sentinel   = Path.Combine(dir, "argus.safemode");
        var safeMode   = new SafeModeController(sentinel);
        safeMode.EnterSafeMode("pre-activated for test");

        var verifier = new ManifestVerifier();
        var logFac   = NullLoggerFactory.Instance;

        // Descriptor pointing at a non-existent exe — must never be launched.
        var descriptors = new[]
        {
            new ChildProcessDescriptor(
                Name:    "FakeChild",
                ExePath: Path.Combine(dir, "DoesNotExist.exe"),
                Args:    Array.Empty<string>()),
        };

        var svc = new SupervisorService(
            descriptors, verifier, safeMode, logFac,
            NullLogger<SupervisorService>.Instance);

        // Should complete without throwing even though the exe doesn't exist.
        var act = async () => await svc.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        // StopAsync on an empty child list must also be safe.
        await svc.StopAsync(CancellationToken.None);
    }

    // ── Process-spawn integration tests ───────────────────────────────────────

    [SkippableFact]
    public async Task Supervisor_Spawns_StubChild_And_It_Runs()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "Process-spawn tests are Windows-only");

        var stubExe = ResolveStubChildExe();
        Skip.If(stubExe is null, "StubChild.exe not built — run 'dotnet build tests/StubChild' first");

        var dir      = CreateTempDir();
        var sentinel = Path.Combine(dir, "argus.safemode");
        var safeMode = new SafeModeController(sentinel);
        var verifier = new ManifestVerifier();
        var logFac   = NullLoggerFactory.Instance;

        // Ask StubChild to emit 10 heartbeats then exit naturally.
        var descriptors = new[]
        {
            new ChildProcessDescriptor(
                Name:    "StubChild",
                ExePath: stubExe!,
                Args:    new[] { "10" }),
        };

        var svc = new SupervisorService(
            descriptors, verifier, safeMode, logFac,
            NullLogger<SupervisorService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await svc.StartAsync(cts.Token);

        // Give the child a couple of seconds to start and emit at least one heartbeat.
        await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);

        // Supervisor must not have entered safe mode (child ran cleanly).
        safeMode.IsSafeMode.Should().BeFalse();

        await svc.StopAsync(CancellationToken.None);
    }

    [SkippableFact]
    public async Task Supervisor_Activates_SafeMode_After_MaxFailures()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "Process-spawn tests are Windows-only");

        var stubExe = ResolveStubChildExe();
        Skip.If(stubExe is null, "StubChild.exe not built — run 'dotnet build tests/StubChild' first");

        var dir      = CreateTempDir();
        var sentinel = Path.Combine(dir, "argus.safemode");
        var safeMode = new SafeModeController(sentinel);
        var verifier = new ManifestVerifier();
        var logFac   = NullLoggerFactory.Instance;

        // StubChild with 0 heartbeats exits immediately — simulates a crashing child.
        var descriptors = new[]
        {
            new ChildProcessDescriptor(
                Name:    "CrashingChild",
                ExePath: stubExe!,
                Args:    new[] { "0" }),
        };

        var svc = new SupervisorService(
            descriptors, verifier, safeMode, logFac,
            NullLogger<SupervisorService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await svc.StartAsync(cts.Token);

        // Wait long enough for MaxConsecutiveRestartFailures (5) to be reached.
        // Each restart is near-instant (no heartbeat → no backoff delay accumulates
        // before SafeMode, but RestartPolicy uses 1 s base). Allow up to 30 s.
        var sw = Stopwatch.StartNew();
        while (!safeMode.IsSafeMode && sw.Elapsed < TimeSpan.FromSeconds(30))
            await Task.Delay(500, CancellationToken.None);

        safeMode.IsSafeMode.Should().BeTrue("supervisor should enter safe mode after max failures");

        await svc.StopAsync(CancellationToken.None);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private readonly List<string> _tempDirs = new();

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"argus-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    /// <summary>
    /// Resolves the StubChild.exe output path relative to the test assembly,
    /// searching common Debug/Release output locations.
    /// Returns null if not found.
    /// </summary>
    private static string? ResolveStubChildExe()
    {
        // Walk up from the test assembly directory to find the repo root,
        // then look under tests/StubChild/bin/.
        var testDir = Path.GetDirectoryName(typeof(SupervisorIntegrationTests).Assembly.Location)!;

        // Typical layout:
        //   tests/Argus.Watchdog.Tests/bin/<config>/net8.0-windows/
        // Repo root is 3 levels up (bin/<config>/net8.0-windows → tests/... → repo root).
        var candidate = testDir;
        for (int i = 0; i < 5; i++)
        {
            candidate = Path.GetDirectoryName(candidate);
            if (candidate is null) break;

            foreach (var config in new[] { "Debug", "Release" })
            {
                var exe = Path.Combine(candidate,
                    "tests", "StubChild", "bin", config, "net8.0", "StubChild.exe");
                if (File.Exists(exe)) return exe;
            }
        }

        return null;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }
}
