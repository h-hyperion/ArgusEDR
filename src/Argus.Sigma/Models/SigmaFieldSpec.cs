namespace Argus.Sigma.Models;

public sealed record SigmaFieldSpec(string FieldName, IReadOnlyList<string> Modifiers)
{
    public static SigmaFieldSpec Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Field spec cannot be empty", nameof(raw));

        var parts = raw.Split('|');
        var name = parts[0].Trim();
        if (name.Length == 0)
            throw new ArgumentException("Field name cannot be empty", nameof(raw));

        var mods = parts.Skip(1).Select(p => p.Trim().ToLowerInvariant()).ToArray();
        return new SigmaFieldSpec(name, mods);
    }
}
