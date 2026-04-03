using Argus.Recovery;
using FluentAssertions;

namespace Argus.Recovery.Tests;

public class CanaryManagerTests
{
    [Fact]
    public async Task PlantCanaries_ThenVerify_ReturnsIntact()
    {
        var dir = Directory.CreateTempSubdirectory("canary_test_").FullName;
        var manager = new CanaryManager(dir);

        await manager.PlantAsync();
        var intact = await manager.VerifyAsync();

        intact.Should().BeTrue();
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task VerifyCanaries_AfterTampering_ReturnsFalse()
    {
        var dir = Directory.CreateTempSubdirectory("canary_test_").FullName;
        var manager = new CanaryManager(dir);

        await manager.PlantAsync();

        // Simulate attacker modifying a canary
        var canaryFiles = Directory.GetFiles(dir, "*.canary", SearchOption.AllDirectories);
        canaryFiles.Should().NotBeEmpty();
        File.SetAttributes(canaryFiles[0], FileAttributes.Normal);
        await File.WriteAllTextAsync(canaryFiles[0], "tampered by attacker");

        var intact = await manager.VerifyAsync();
        intact.Should().BeFalse();

        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task VerifyCanaries_AfterDeletion_ReturnsFalse()
    {
        var dir = Directory.CreateTempSubdirectory("canary_test_").FullName;
        var manager = new CanaryManager(dir);

        await manager.PlantAsync();
        var canaryFiles = Directory.GetFiles(dir, "*.canary", SearchOption.AllDirectories);
        File.Delete(canaryFiles[0]);

        var intact = await manager.VerifyAsync();
        intact.Should().BeFalse();

        Directory.Delete(dir, true);
    }
}
