using Argus.Defender.Dns;
using FluentAssertions;
using Moq;

namespace Argus.Defender.Tests.Dns;

public class DnsProtectionTests
{
    private readonly Mock<IDnsNativeApi> _mockApi;
    private readonly DnsProtectionService _service;

    public DnsProtectionTests()
    {
        _mockApi = new Mock<IDnsNativeApi>();
        _service = new DnsProtectionService(_mockApi.Object);
    }

    [Fact]
    public void Constructor_InitializesWithSystemProfile()
    {
        _service.CurrentProfile.Should().Be(DnsProfile.System);
    }

    [Fact]
    public void Apply_WithPrivacyProfile_SetsCloudflareDns()
    {
        _mockApi.Setup(x => x.GetNetworkAdapterNames())
            .Returns(new[] { "Ethernet", "Wi-Fi" });

        _service.Apply(DnsProfile.Privacy);

        _service.CurrentProfile.Should().Be(DnsProfile.Privacy);
        _mockApi.Verify(x => x.SetDnsServers("Ethernet", "1.1.1.1", "1.0.0.1"), Times.Once);
        _mockApi.Verify(x => x.SetDnsServers("Wi-Fi", "1.1.1.1", "1.0.0.1"), Times.Once);
    }

    [Fact]
    public void Apply_WithMalwareBlockingProfile_SetsBlockingDns()
    {
        _mockApi.Setup(x => x.GetNetworkAdapterNames())
            .Returns(new[] { "Ethernet" });

        _service.Apply(DnsProfile.MalwareBlocking);

        _service.CurrentProfile.Should().Be(DnsProfile.MalwareBlocking);
        _mockApi.Verify(x => x.SetDnsServers("Ethernet", "1.1.1.2", "1.0.0.2"), Times.Once);
    }

    [Fact]
    public void Apply_WithFamilyProfile_SetsFamilyDns()
    {
        _mockApi.Setup(x => x.GetNetworkAdapterNames())
            .Returns(new[] { "Wi-Fi" });

        _service.Apply(DnsProfile.Family);

        _service.CurrentProfile.Should().Be(DnsProfile.Family);
        _mockApi.Verify(x => x.SetDnsServers("Wi-Fi", "1.1.1.3", "1.0.0.3"), Times.Once);
    }

    [Fact]
    public void Apply_WithSystemProfile_ResetsToAutomatic()
    {
        _mockApi.Setup(x => x.GetNetworkAdapterNames())
            .Returns(new[] { "Ethernet" });

        _service.Apply(DnsProfile.System);

        _service.CurrentProfile.Should().Be(DnsProfile.System);
        _mockApi.Verify(x => x.ResetDnsToAutomatic("Ethernet"), Times.Once);
    }

    [Fact]
    public void Apply_WithMultipleAdapters_SetsAllAdapters()
    {
        var adapters = new[] { "Ethernet", "Wi-Fi", "VMware Network Adapter" };
        _mockApi.Setup(x => x.GetNetworkAdapterNames())
            .Returns(adapters);

        _service.Apply(DnsProfile.Privacy);

        _mockApi.Verify(x => x.SetDnsServers(It.IsAny<string>(), "1.1.1.1", "1.0.0.1"), Times.Exactly(3));
    }

    [Fact]
    public void Apply_WhenAdapterThrows_ContinuesToNextAdapter()
    {
        _mockApi.Setup(x => x.GetNetworkAdapterNames())
            .Returns(new[] { "Ethernet", "Wi-Fi" });

        _mockApi.Setup(x => x.SetDnsServers("Ethernet", It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new Exception("Network error"));

        _service.Apply(DnsProfile.Privacy);

        _mockApi.Verify(x => x.SetDnsServers("Wi-Fi", "1.1.1.1", "1.0.0.1"), Times.Once);
    }

    [Fact]
    public void Apply_WhenNoAdapters_DoesNotThrow()
    {
        _mockApi.Setup(x => x.GetNetworkAdapterNames())
            .Returns(Array.Empty<string>());

        var action = () => _service.Apply(DnsProfile.Privacy);

        action.Should().NotThrow();
        _service.CurrentProfile.Should().Be(DnsProfile.Privacy);
    }
}
