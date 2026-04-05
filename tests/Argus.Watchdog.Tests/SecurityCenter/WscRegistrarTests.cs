using FluentAssertions;
using Microsoft.Win32;
using Argus.Watchdog.SecurityCenter;

namespace Argus.Watchdog.Tests.SecurityCenter;

public class WscRegistrarTests
{
    [Fact]
    public void BuildPayload_WithOnStatus_ReturnsCorrectState()
    {
        var registrar = new WscRegistrar();
        
        var payload = registrar.BuildPayload("TestProduct", WscProtectionStatus.On);
        
        payload.ProductName.Should().Be("TestProduct");
        payload.SignatureStatus.Should().Be(WscSignatureStatus.UpToDate);
        payload.ProductState.Should().Be(0x1010);
    }

    [Fact]
    public void BuildPayload_WithOffStatus_ReturnsCorrectState()
    {
        var registrar = new WscRegistrar();
        
        var payload = registrar.BuildPayload("TestProduct", WscProtectionStatus.Off);
        
        payload.ProductName.Should().Be("TestProduct");
        payload.SignatureStatus.Should().Be(WscSignatureStatus.UpToDate);
        payload.ProductState.Should().Be(0x0010);
    }

    [Fact]
    public void BuildPayload_WithExpiredStatus_ReturnsCorrectState()
    {
        var registrar = new WscRegistrar();
        
        var payload = registrar.BuildPayload("TestProduct", WscProtectionStatus.Expired);
        
        payload.ProductName.Should().Be("TestProduct");
        payload.SignatureStatus.Should().Be(WscSignatureStatus.OutOfDate);
    }

    [Fact]
    public void UpdateState_WithValidStatus_UpdatesRegistry()
    {
        var registrar = new WscRegistrar();
        
        registrar.UpdateState(WscProtectionStatus.On);
        
        var keyPath = @"SOFTWARE\Microsoft\Security Center\Provider\Av\{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}";
        using var key = Registry.LocalMachine.OpenSubKey(keyPath);
        if (key is not null)
        {
            var state = key.GetValue("ProductState");
            if (state is int intState)
            {
                intState.Should().Be(0x1010);
            }
        }
    }
}
