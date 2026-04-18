namespace Argus.Sigma.Conditions;

public sealed record AndNode(SigmaCondition Left, SigmaCondition Right) : SigmaCondition;
