namespace Argus.Sigma.Conditions;

public enum Quantifier { OneOf, AllOf }

public sealed record QuantifierNode(Quantifier Kind, IReadOnlyList<string> SelectionNames) : SigmaCondition;
