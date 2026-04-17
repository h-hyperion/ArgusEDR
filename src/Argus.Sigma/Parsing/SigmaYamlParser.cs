using Argus.Sigma.Models;
using YamlDotNet.RepresentationModel;

namespace Argus.Sigma.Parsing;

public static class SigmaYamlParser
{
    public static SigmaRule Parse(string yamlText, string? sourceFile = null)
    {
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yamlText);
            stream.Load(reader);
            if (stream.Documents.Count == 0)
                throw new SigmaParseException("Empty rule document", sourceFile);

            var root = (YamlMappingNode)stream.Documents[0].RootNode;

            var title = RequireScalar(root, "title", sourceFile);
            var id = RequireScalar(root, "id", sourceFile);
            var description = OptionalScalar(root, "description");
            var author = OptionalScalar(root, "author");
            var level = ParseLevel(OptionalScalar(root, "level"));
            var logSource = ParseLogSource(Require(root, "logsource", sourceFile), sourceFile);
            var detection = ParseDetection(Require(root, "detection", sourceFile), sourceFile);
            var tags = ReadSequence(root, "tags");
            var fps = ReadSequence(root, "falsepositives");
            var refs = ReadSequence(root, "references");

            return new SigmaRule(id, title, description, level, logSource, detection, tags, fps, refs, author);
        }
        catch (SigmaParseException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SigmaParseException($"Failed to parse Sigma rule: {ex.Message}", sourceFile, ex);
        }
    }

    private static YamlNode Require(YamlMappingNode node, string key, string? src)
    {
        var k = new YamlScalarNode(key);
        if (!node.Children.TryGetValue(k, out var v))
            throw new SigmaParseException($"Rule is missing required field '{key}'", src);
        return v;
    }

    private static string RequireScalar(YamlMappingNode node, string key, string? src)
    {
        if (Require(node, key, src) is YamlScalarNode s && s.Value is not null)
            return s.Value;
        throw new SigmaParseException($"Field '{key}' must be a scalar", src);
    }

    private static string? OptionalScalar(YamlMappingNode node, string key)
    {
        var k = new YamlScalarNode(key);
        return node.Children.TryGetValue(k, out var v) && v is YamlScalarNode s ? s.Value : null;
    }

    private static IReadOnlyList<string> ReadSequence(YamlMappingNode node, string key)
    {
        var k = new YamlScalarNode(key);
        if (!node.Children.TryGetValue(k, out var v)) return Array.Empty<string>();
        if (v is YamlSequenceNode seq)
            return seq.Children.OfType<YamlScalarNode>().Select(s => s.Value!).Where(s => s is not null).ToArray();
        return Array.Empty<string>();
    }

    private static SigmaLevel ParseLevel(string? raw) => raw?.ToLowerInvariant() switch
    {
        "informational" or "info" => SigmaLevel.Informational,
        "low" => SigmaLevel.Low,
        null or "" or "medium" => SigmaLevel.Medium,
        "high" => SigmaLevel.High,
        "critical" => SigmaLevel.Critical,
        _ => SigmaLevel.Medium
    };

    private static SigmaLogSource ParseLogSource(YamlNode node, string? src)
    {
        if (node is not YamlMappingNode map)
            throw new SigmaParseException("'logsource' must be a mapping", src);
        return new SigmaLogSource(
            Category: OptionalScalar(map, "category"),
            Product: OptionalScalar(map, "product"),
            Service: OptionalScalar(map, "service"));
    }

    private static SigmaDetection ParseDetection(YamlNode node, string? src)
    {
        if (node is not YamlMappingNode map)
            throw new SigmaParseException("'detection' must be a mapping", src);
        var conditionNode = map.Children.TryGetValue(new YamlScalarNode("condition"), out var c) ? c : null;
        if (conditionNode is not YamlScalarNode condScalar || condScalar.Value is null)
            throw new SigmaParseException("'detection.condition' is required and must be a scalar", src);

        var selections = new Dictionary<string, SigmaSelection>(StringComparer.Ordinal);
        foreach (var pair in map.Children)
        {
            if (pair.Key is not YamlScalarNode sk || sk.Value == "condition") continue;
            var selName = sk.Value!;
            if (pair.Value is not YamlMappingNode selMap)
                throw new SigmaParseException($"Selection '{selName}' must be a mapping", src);

            var fields = new Dictionary<SigmaFieldSpec, IReadOnlyList<string>>();
            foreach (var fieldPair in selMap.Children)
            {
                if (fieldPair.Key is not YamlScalarNode fk || fk.Value is null)
                    throw new SigmaParseException($"Field key in selection '{selName}' must be a scalar", src);
                var spec = SigmaFieldSpec.Parse(fk.Value);
                var values = ReadFieldValues(fieldPair.Value, selName, src);
                fields[spec] = values;
            }
            selections[selName] = new SigmaSelection(selName, fields);
        }

        if (selections.Count == 0)
            throw new SigmaParseException("'detection' must contain at least one selection block", src);

        return new SigmaDetection(selections, condScalar.Value);
    }

    private static IReadOnlyList<string> ReadFieldValues(YamlNode node, string selection, string? src) => node switch
    {
        YamlScalarNode s => new[] { s.Value ?? "" },
        YamlSequenceNode seq => seq.Children.Select(c => c is YamlScalarNode sc
            ? sc.Value ?? ""
            : throw new SigmaParseException($"Nested non-scalar in selection '{selection}'; nested lists are not supported in v2.2", src)).ToArray(),
        _ => throw new SigmaParseException($"Field value in selection '{selection}' must be scalar or list of scalars", src)
    };
}
