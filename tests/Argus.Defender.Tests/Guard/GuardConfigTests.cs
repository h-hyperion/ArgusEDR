using Argus.Defender.Guard;
using FluentAssertions;
using System.Text.Json;

namespace Argus.Defender.Tests.Guard;

public class GuardConfigTests
{
    [Fact]
    public void GuardConfig_Deserializes_AllToggles()
    {
        var json = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Guard", "GuardConfig.json"));
        var config = JsonSerializer.Deserialize<GuardConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        config.Should().NotBeNull();
        config!.Toggles.Should().HaveCount(26);
        config.Toggles.Select(t => t.Id).Distinct().Should().HaveCount(26);
    }

    [Fact]
    public void GuardToggle_HasRequiredFields()
    {
        var toggle = new GuardToggle
        {
            Id = "telemetry_diagtrack",
            Name = "Connected User Experiences and Telemetry",
            Category = "Telemetry",
            Enabled = false
        };

        toggle.Id.Should().NotBeNullOrEmpty();
        toggle.Name.Should().NotBeNullOrEmpty();
        toggle.Category.Should().NotBeNullOrEmpty();
    }
}
