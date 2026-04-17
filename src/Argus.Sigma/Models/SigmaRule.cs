namespace Argus.Sigma.Models;

public sealed record SigmaRule(
    string Id,
    string Title,
    string? Description,
    SigmaLevel Level,
    SigmaLogSource LogSource,
    SigmaDetection Detection,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> FalsePositives,
    IReadOnlyList<string> References,
    string? Author);
