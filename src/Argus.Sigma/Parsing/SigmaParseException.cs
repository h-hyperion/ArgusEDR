namespace Argus.Sigma.Parsing;

public sealed class SigmaParseException : Exception
{
    public string? RuleFile { get; }

    public SigmaParseException(string message, string? ruleFile = null, Exception? inner = null)
        : base(message, inner)
    {
        RuleFile = ruleFile;
    }
}
