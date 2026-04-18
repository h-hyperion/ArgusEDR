using Argus.Sigma.Conditions;
using Argus.Sigma.Matching;
using Argus.Sigma.Models;

namespace Argus.Sigma.Engine;

public sealed class CompiledRule
{
    public SigmaRule Source { get; }
    private readonly SigmaCondition _condition;
    private readonly IReadOnlyDictionary<string, SelectionEvaluator> _selections;

    internal CompiledRule(SigmaRule source, SigmaCondition condition, IReadOnlyDictionary<string, SelectionEvaluator> selections)
    {
        Source = source;
        _condition = condition;
        _selections = selections;
    }

    public bool Evaluate(SigmaEventContext ctx) => EvaluateNode(_condition, ctx);

    private bool EvaluateNode(SigmaCondition node, SigmaEventContext ctx) => node switch
    {
        SelectionRef sr => _selections[sr.Name].Evaluate(ctx),
        AndNode an => EvaluateNode(an.Left, ctx) && EvaluateNode(an.Right, ctx),
        OrNode on => EvaluateNode(on.Left, ctx) || EvaluateNode(on.Right, ctx),
        NotNode nn => !EvaluateNode(nn.Inner, ctx),
        QuantifierNode qn => qn.Kind == Quantifier.OneOf
            ? qn.SelectionNames.Any(s => _selections[s].Evaluate(ctx))
            : qn.SelectionNames.All(s => _selections[s].Evaluate(ctx)),
        _ => throw new InvalidOperationException($"Unknown condition node: {node.GetType().Name}")
    };
}

internal sealed class SelectionEvaluator
{
    private readonly IReadOnlyList<(string FieldName, IFieldMatcher Matcher)> _fields;

    public SelectionEvaluator(IReadOnlyList<(string FieldName, IFieldMatcher Matcher)> fields)
    {
        _fields = fields;
    }

    public bool Evaluate(SigmaEventContext ctx) =>
        _fields.All(f => f.Matcher.Match(ctx.GetField(f.FieldName)));
}
