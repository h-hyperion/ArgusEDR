namespace Argus.Sigma.Engine;

public sealed class ConsoleDetectionSink : IDetectionSink
{
    public void Emit(SigmaMatch match)
    {
        Console.WriteLine($"[SIGMA] {match.MatchedAtUtc:O} level={match.Rule.Level} rule=\"{match.Rule.Title}\" id={match.Rule.Id}");
    }
}
