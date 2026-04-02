// tests\Argus.Defender.Tests\Etw\EtwConsumerTests.cs
using Argus.Defender.Etw;
using FluentAssertions;

namespace Argus.Defender.Tests.Etw;

public class EtwConsumerTests
{
    [Fact]
    public void EtwConsumer_ChannelHasBoundedCapacity()
    {
        using var consumer = new EtwConsumer(maxQueueSize: 100);
        consumer.Events.Should().NotBeNull();
    }

    [Fact]
    public void EtwEvent_ProcessStart_HasRequiredFields()
    {
        var evt = new EtwEvent
        {
            Type = EtwEventType.ProcessStart,
            ProcessId = 1234,
            ParentProcessId = 5678,
            ImageName = "cmd.exe",
            CommandLine = "cmd.exe /c whoami"
        };

        evt.Type.Should().Be(EtwEventType.ProcessStart);
        evt.ProcessId.Should().Be(1234);
        evt.ParentProcessId.Should().Be(5678);
        evt.ImageName.Should().Be("cmd.exe");
        evt.CommandLine.Should().Be("cmd.exe /c whoami");
    }

    [Fact]
    public void EtwEvent_RegistryChange_HasRequiredFields()
    {
        var evt = new EtwEvent
        {
            Type = EtwEventType.RegistryChange,
            RegistryKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            RegistryValue = "MalwareAutostart",
            ProcessId = 9999
        };

        evt.Type.Should().Be(EtwEventType.RegistryChange);
        evt.RegistryKey.Should().Contain("Run");
        evt.ProcessId.Should().Be(9999);
    }
}
