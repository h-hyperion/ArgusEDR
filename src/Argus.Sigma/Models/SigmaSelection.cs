namespace Argus.Sigma.Models;

public sealed record SigmaSelection(
    string Name,
    IReadOnlyDictionary<SigmaFieldSpec, IReadOnlyList<string>> Fields);
