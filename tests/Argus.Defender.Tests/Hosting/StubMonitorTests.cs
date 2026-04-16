using Argus.Defender.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Argus.Defender.Tests.Hosting;

public sealed class StubMonitorTests
{
    [Fact]
    public void AmsiMonitorHost_HasCorrectIdentity()
    {
        var monitor = new AmsiMonitorHost();
        Assert.Equal("amsi", monitor.Id);
        Assert.Equal("AMSI Script Interception", monitor.DisplayName);
    }

    [Fact]
    public void AmsiMonitorHost_AlwaysReturnsNotImplemented()
    {
        var monitor = new AmsiMonitorHost();
        Assert.Equal(MonitorStatus.NotImplemented, monitor.Status);
    }

    [Fact]
    public void AmsiMonitorHost_EventsEmittedIsZero()
    {
        var monitor = new AmsiMonitorHost();
        Assert.Equal(0L, monitor.EventsEmitted);
    }

    [Fact]
    public async Task AmsiMonitorHost_EnableAsyncSetsIsEnabled()
    {
        var monitor = new AmsiMonitorHost();
        Assert.False(monitor.IsEnabled);

        await monitor.EnableAsync(CancellationToken.None);

        Assert.True(monitor.IsEnabled);
    }

    [Fact]
    public async Task AmsiMonitorHost_DisableAsyncClearsIsEnabled()
    {
        var monitor = new AmsiMonitorHost();
        await monitor.EnableAsync(CancellationToken.None);
        Assert.True(monitor.IsEnabled);

        await monitor.DisableAsync(CancellationToken.None);

        Assert.False(monitor.IsEnabled);
    }

    [Fact]
    public async Task AmsiMonitorHost_EnableDisableDoNotThrow()
    {
        var monitor = new AmsiMonitorHost();

        // Should not throw.
        await monitor.EnableAsync(CancellationToken.None);
        await monitor.DisableAsync(CancellationToken.None);
        await monitor.EnableAsync(CancellationToken.None);
    }

    [Fact]
    public void ByovdMonitorHost_HasCorrectIdentity()
    {
        var monitor = new ByovdMonitorHost();
        Assert.Equal("byovd", monitor.Id);
        Assert.Equal("BYOVD Driver Detection", monitor.DisplayName);
    }

    [Fact]
    public void ByovdMonitorHost_AlwaysReturnsNotImplemented()
    {
        var monitor = new ByovdMonitorHost();
        Assert.Equal(MonitorStatus.NotImplemented, monitor.Status);
    }

    [Fact]
    public void ByovdMonitorHost_EventsEmittedIsZero()
    {
        var monitor = new ByovdMonitorHost();
        Assert.Equal(0L, monitor.EventsEmitted);
    }

    [Fact]
    public async Task ByovdMonitorHost_EnableAsyncSetsIsEnabled()
    {
        var monitor = new ByovdMonitorHost();
        Assert.False(monitor.IsEnabled);

        await monitor.EnableAsync(CancellationToken.None);

        Assert.True(monitor.IsEnabled);
    }

    [Fact]
    public async Task ByovdMonitorHost_DisableAsyncClearsIsEnabled()
    {
        var monitor = new ByovdMonitorHost();
        await monitor.EnableAsync(CancellationToken.None);
        Assert.True(monitor.IsEnabled);

        await monitor.DisableAsync(CancellationToken.None);

        Assert.False(monitor.IsEnabled);
    }

    [Fact]
    public async Task ByovdMonitorHost_EnableDisableDoNotThrow()
    {
        var monitor = new ByovdMonitorHost();

        // Should not throw.
        await monitor.EnableAsync(CancellationToken.None);
        await monitor.DisableAsync(CancellationToken.None);
        await monitor.EnableAsync(CancellationToken.None);
    }

    [Fact]
    public void QuarantineMaintenanceHost_HasCorrectIdentity()
    {
        var logger = NullLogger<QuarantineMaintenanceHost>.Instance;
        var monitor = new QuarantineMaintenanceHost(logger);
        Assert.Equal("quarantine-maintenance", monitor.Id);
        Assert.Equal("Quarantine Maintenance", monitor.DisplayName);
    }

    [Fact]
    public void QuarantineMaintenanceHost_StartsInStoppedStatus()
    {
        var logger = NullLogger<QuarantineMaintenanceHost>.Instance;
        var monitor = new QuarantineMaintenanceHost(logger);
        Assert.Equal(MonitorStatus.Stopped, monitor.Status);
    }

    [Fact]
    public void QuarantineMaintenanceHost_EventsEmittedStartsAtZero()
    {
        var logger = NullLogger<QuarantineMaintenanceHost>.Instance;
        var monitor = new QuarantineMaintenanceHost(logger);
        Assert.Equal(0L, monitor.EventsEmitted);
    }

    [Fact]
    public async Task QuarantineMaintenanceHost_EnableAsyncSetsRunningStatus()
    {
        var logger = NullLogger<QuarantineMaintenanceHost>.Instance;
        var monitor = new QuarantineMaintenanceHost(logger);
        Assert.Equal(MonitorStatus.Stopped, monitor.Status);

        await monitor.EnableAsync(CancellationToken.None);

        Assert.Equal(MonitorStatus.Running, monitor.Status);
        Assert.True(monitor.IsEnabled);
    }

    [Fact]
    public async Task QuarantineMaintenanceHost_DisableAsyncSetsStoppedStatus()
    {
        var logger = NullLogger<QuarantineMaintenanceHost>.Instance;
        var monitor = new QuarantineMaintenanceHost(logger);
        await monitor.EnableAsync(CancellationToken.None);
        Assert.Equal(MonitorStatus.Running, monitor.Status);

        await monitor.DisableAsync(CancellationToken.None);

        Assert.Equal(MonitorStatus.Stopped, monitor.Status);
        Assert.False(monitor.IsEnabled);
    }
}
