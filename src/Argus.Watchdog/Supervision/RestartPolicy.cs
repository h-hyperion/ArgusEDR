namespace Argus.Watchdog.Supervision;

using Argus.Core;

/// <summary>
/// Implements exponential backoff for restart attempts with safe mode triggering.
/// </summary>
public sealed class RestartPolicy
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the number of consecutive failures since the last success.
    /// </summary>
    public int ConsecutiveFailures { get; private set; }

    /// <summary>
    /// Gets the recommended delay before the next restart attempt.
    /// Implements exponential backoff: 1s, 2s, 4s, 8s, 16s, then capped at 30s.
    /// </summary>
    public TimeSpan NextDelay
    {
        get
        {
            if (ConsecutiveFailures <= 0) return BaseDelay;
            var seconds = Math.Min(
                Math.Pow(2, ConsecutiveFailures - 1),
                MaxDelay.TotalSeconds);
            return TimeSpan.FromSeconds(seconds);
        }
    }

    /// <summary>
    /// Gets a value indicating whether safe mode should be triggered.
    /// This occurs after exceeding MaxConsecutiveRestartFailures consecutive failures.
    /// </summary>
    public bool ShouldTriggerSafeMode =>
        ConsecutiveFailures >= ArgusConstants.MaxConsecutiveRestartFailures;

    /// <summary>
    /// Records a restart failure and increments the consecutive failure counter.
    /// </summary>
    public void RegisterFailure() => ConsecutiveFailures++;

    /// <summary>
    /// Records a successful restart and resets the failure counter.
    /// </summary>
    public void RegisterSuccess() => ConsecutiveFailures = 0;
}
