using Argus.Sigma.Conditions;
using Argus.Sigma.Engine;
using Argus.Sigma.Matching;
using Argus.Sigma.Models;

namespace Argus.Sigma.Parsing;

public static class SigmaCompiler
{
    private static readonly HashSet<string> KnownModifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "contains", "startswith", "endswith", "re", "all"
    };

    public static CompiledRule Compile(SigmaRule rule)
    {
        var selectionNames = rule.Detection.Selections.Keys.ToArray();
        var ast = SigmaConditionParser.Parse(rule.Detection.Condition, selectionNames);

        var evaluators = new Dictionary<string, SelectionEvaluator>(StringComparer.Ordinal);
        foreach (var (name, selection) in rule.Detection.Selections)
        {
            evaluators[name] = CompileSelection(selection, rule.Id);
        }

        return new CompiledRule(rule, ast, evaluators);
    }

    private static SelectionEvaluator CompileSelection(SigmaSelection selection, string ruleId)
    {
        var compiledFields = new List<(string FieldName, IFieldMatcher Matcher)>(selection.Fields.Count);

        foreach (var (spec, values) in selection.Fields)
        {
            foreach (var mod in spec.Modifiers)
            {
                if (!KnownModifiers.Contains(mod))
                    throw new SigmaParseException($"Rule '{ruleId}' uses unknown modifier '{mod}' on field '{spec.FieldName}'");
            }

            var matchKind = spec.Modifiers.FirstOrDefault(m => m is "contains" or "startswith" or "endswith" or "re") ?? "equals";
            var isAll = spec.Modifiers.Contains("all", StringComparer.OrdinalIgnoreCase);

            var valueMatchers = values.Select(v => BuildMatcher(matchKind, v)).ToArray();
            var combined = valueMatchers.Length == 1
                ? valueMatchers[0]
                : isAll ? FieldMatchers.All(valueMatchers) : FieldMatchers.AnyOf(valueMatchers);

            compiledFields.Add((spec.FieldName, combined));
        }

        return new SelectionEvaluator(compiledFields);
    }

    private static IFieldMatcher BuildMatcher(string kind, string value) => kind switch
    {
        "contains" => FieldMatchers.Contains(value),
        "startswith" => FieldMatchers.StartsWith(value),
        "endswith" => FieldMatchers.EndsWith(value),
        "re" => FieldMatchers.Regex(value),
        _ => FieldMatchers.Equals(value)
    };
}
