using Argus.Defender.Guard;
using FluentAssertions;
using Moq;

namespace Argus.Defender.Tests.Guard;

public class GuardEnforcerTests
{
    [Fact]
    public void ApplyToggle_DisabledToggle_IsSkipped()
    {
        var mockApi = new Mock<IWindowsPrivacyApi>();
        var enforcer = new GuardEnforcer(mockApi.Object);
        var toggle = new GuardToggle { Id = "telemetry_diagtrack", Enabled = false };

        enforcer.Apply(toggle);

        mockApi.Verify(a => a.SetRegistryValue(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<object>()), Times.Never);
        mockApi.Verify(a => a.StopAndDisableService(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ApplyToggle_DiagTrackEnabled_DisablesService()
    {
        var mockApi = new Mock<IWindowsPrivacyApi>();
        var enforcer = new GuardEnforcer(mockApi.Object);
        var toggle = new GuardToggle { Id = "telemetry_diagtrack", Enabled = true };

        enforcer.Apply(toggle);

        mockApi.Verify(a => a.StopAndDisableService("DiagTrack"), Times.Once);
    }

    [Fact]
    public void ApplyToggle_AdvertisingId_SetsRegistryKey()
    {
        var mockApi = new Mock<IWindowsPrivacyApi>();
        var enforcer = new GuardEnforcer(mockApi.Object);
        var toggle = new GuardToggle { Id = "ads_advertising_id", Enabled = true };

        enforcer.Apply(toggle);

        mockApi.Verify(a => a.SetRegistryValue(
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
            "Enabled", 0), Times.Once);
    }

    [Fact]
    public void ApplyAll_AppliesOnlyEnabledToggles()
    {
        var mockApi = new Mock<IWindowsPrivacyApi>();
        var enforcer = new GuardEnforcer(mockApi.Object);
        var config = new GuardConfig
        {
            Toggles = new List<GuardToggle>
            {
                new() { Id = "telemetry_diagtrack", Enabled = true },
                new() { Id = "telemetry_location", Enabled = false },
                new() { Id = "ads_advertising_id", Enabled = true }
            }
        };

        enforcer.ApplyAll(config);

        mockApi.Verify(a => a.StopAndDisableService("DiagTrack"), Times.Once);
        mockApi.Verify(a => a.SetRegistryValue(
            It.Is<string>(s => s.Contains("AdvertisingInfo")),
            "Enabled", 0), Times.Once);
        // Location was disabled, should NOT have been called
        mockApi.Verify(a => a.SetRegistryValue(
            It.Is<string>(s => s.Contains("LocationAndSensors")),
            It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }
}
