namespace Argus.Watchdog.Tests.Supervision;

using Argus.Core;
using Argus.Watchdog.Supervision;
using FluentAssertions;

public class RestartPolicyTests
{
    [Fact]
    public void Constructor_InitializesWithDefaultState()
    {
        var policy = new RestartPolicy();

        policy.NextDelay.Should().Be(TimeSpan.FromSeconds(1));
        policy.ShouldTriggerSafeMode.Should().BeFalse();
        policy.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void BackoffSequence_ProducesExponentialBackoff()
    {
        var policy = new RestartPolicy();

        // 1st failure -> 1s
        policy.RegisterFailure();
        policy.NextDelay.Should().Be(TimeSpan.FromSeconds(1));

        // 2nd failure -> 2s
        policy.RegisterFailure();
        policy.NextDelay.Should().Be(TimeSpan.FromSeconds(2));

        // 3rd failure -> 4s
        policy.RegisterFailure();
        policy.NextDelay.Should().Be(TimeSpan.FromSeconds(4));

        // 4th failure -> 8s
        policy.RegisterFailure();
        policy.NextDelay.Should().Be(TimeSpan.FromSeconds(8));

        // 5th failure -> 16s
        policy.RegisterFailure();
        policy.NextDelay.Should().Be(TimeSpan.FromSeconds(16));

        // 6th failure -> 30s (capped)
        policy.RegisterFailure();
        policy.NextDelay.Should().Be(TimeSpan.FromSeconds(30));

        // 7th failure -> 30s (capped)
        policy.RegisterFailure();
        policy.NextDelay.Should().Be(TimeSpan.FromSeconds(30));

        // 8th failure -> 30s (capped)
        policy.RegisterFailure();
        policy.NextDelay.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void SafeModeTrigger_ActivatesAfterMaxConsecutiveFailures()
    {
        var policy = new RestartPolicy();

        // Below threshold
        for (int i = 0; i < ArgusConstants.MaxConsecutiveRestartFailures - 1; i++)
        {
            policy.RegisterFailure();
            policy.ShouldTriggerSafeMode.Should().BeFalse();
        }

        // At threshold
        policy.RegisterFailure();
        policy.ShouldTriggerSafeMode.Should().BeTrue();

        // Beyond threshold
        policy.RegisterFailure();
        policy.ShouldTriggerSafeMode.Should().BeTrue();
    }

    [Fact]
    public void RegisterSuccess_ResetsCounterToZero()
    {
        var policy = new RestartPolicy();

        policy.RegisterFailure();
        policy.RegisterFailure();
        policy.RegisterFailure();
        policy.ConsecutiveFailures.Should().Be(3);

        policy.RegisterSuccess();
        policy.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void RegisterSuccess_ResetsSafeModeFlag()
    {
        var policy = new RestartPolicy();

        // Trigger safe mode
        for (int i = 0; i < ArgusConstants.MaxConsecutiveRestartFailures; i++)
        {
            policy.RegisterFailure();
        }
        policy.ShouldTriggerSafeMode.Should().BeTrue();

        // Reset and verify
        policy.RegisterSuccess();
        policy.ShouldTriggerSafeMode.Should().BeFalse();
    }

    [Fact]
    public void RegisterSuccess_ResetsNextDelayToBase()
    {
        var policy = new RestartPolicy();

        policy.RegisterFailure();
        policy.RegisterFailure();
        policy.RegisterFailure();
        policy.NextDelay.Should().Be(TimeSpan.FromSeconds(4));

        policy.RegisterSuccess();
        policy.NextDelay.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void RegisterSuccess_CanResetAfterSafeModeTriggered()
    {
        var policy = new RestartPolicy();

        // Trigger safe mode
        for (int i = 0; i < ArgusConstants.MaxConsecutiveRestartFailures + 10; i++)
        {
            policy.RegisterFailure();
        }
        policy.ShouldTriggerSafeMode.Should().BeTrue();

        // Reset everything
        policy.RegisterSuccess();
        policy.ConsecutiveFailures.Should().Be(0);
        policy.ShouldTriggerSafeMode.Should().BeFalse();
        policy.NextDelay.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void BackoffSequence_HandlesMultipleResetCycles()
    {
        var policy = new RestartPolicy();

        // First cycle: 2 failures
        policy.RegisterFailure();
        policy.RegisterFailure();
        policy.NextDelay.Should().Be(TimeSpan.FromSeconds(2));

        // Reset
        policy.RegisterSuccess();
        policy.NextDelay.Should().Be(TimeSpan.FromSeconds(1));

        // Second cycle: 3 failures
        policy.RegisterFailure();
        policy.RegisterFailure();
        policy.RegisterFailure();
        policy.NextDelay.Should().Be(TimeSpan.FromSeconds(4));

        // Reset again
        policy.RegisterSuccess();
        policy.NextDelay.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(7, 30)]
    [InlineData(10, 30)]
    public void NextDelay_ComputesCorrectBackoffForFailureCount(int failureCount, int expectedSeconds)
    {
        var policy = new RestartPolicy();

        for (int i = 0; i < failureCount; i++)
        {
            policy.RegisterFailure();
        }

        policy.NextDelay.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void ConsecutiveFailures_TracksAccurately()
    {
        var policy = new RestartPolicy();

        policy.ConsecutiveFailures.Should().Be(0);

        policy.RegisterFailure();
        policy.ConsecutiveFailures.Should().Be(1);

        policy.RegisterFailure();
        policy.ConsecutiveFailures.Should().Be(2);

        policy.RegisterSuccess();
        policy.ConsecutiveFailures.Should().Be(0);

        policy.RegisterFailure();
        policy.ConsecutiveFailures.Should().Be(1);
    }
}
