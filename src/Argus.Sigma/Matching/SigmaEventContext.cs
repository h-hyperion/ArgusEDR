namespace Argus.Sigma.Matching;

public sealed class SigmaEventContext
{
    private readonly Dictionary<string, string?> _fields;

    public string Category { get; }

    public SigmaEventContext(string category, IReadOnlyDictionary<string, string?> fields)
    {
        Category = category ?? throw new ArgumentNullException(nameof(category));
        _fields = new Dictionary<string, string?>(fields, StringComparer.OrdinalIgnoreCase);
    }

    public string? GetField(string name) =>
        _fields.TryGetValue(name, out var v) ? v : null;
}
