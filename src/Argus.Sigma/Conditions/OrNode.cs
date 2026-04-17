namespace Argus.Sigma.Conditions;

public sealed record OrNode(SigmaCondition Left, SigmaCondition Right) : SigmaCondition;
