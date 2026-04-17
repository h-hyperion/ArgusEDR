namespace Argus.Sigma.Models;

public sealed record SigmaDetection(
    IReadOnlyDictionary<string, SigmaSelection> Selections,
    string Condition);
