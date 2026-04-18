using System.Text.RegularExpressions;

namespace Argus.Sigma.Matching;

public static class FieldMatchers
{
    private const StringComparison Cmp = StringComparison.OrdinalIgnoreCase;

    public static IFieldMatcher Equals(string expected) => new EqualsMatcher(expected);
    public static IFieldMatcher Contains(string needle) => new ContainsMatcher(needle);
    public static IFieldMatcher StartsWith(string prefix) => new StartsWithMatcher(prefix);
    public static IFieldMatcher EndsWith(string suffix) => new EndsWithMatcher(suffix);
    public static IFieldMatcher Regex(string pattern) => new RegexMatcher(pattern);
    public static IFieldMatcher AnyOf(IEnumerable<IFieldMatcher> inner) => new AnyOfMatcher(inner.ToArray());
    public static IFieldMatcher All(IEnumerable<IFieldMatcher> inner) => new AllMatcher(inner.ToArray());

    private sealed class EqualsMatcher(string expected) : IFieldMatcher
    {
        public bool Match(string? value) => value is not null && string.Equals(value, expected, Cmp);
    }

    private sealed class ContainsMatcher(string needle) : IFieldMatcher
    {
        public bool Match(string? value) => value is not null && value.Contains(needle, Cmp);
    }

    private sealed class StartsWithMatcher(string prefix) : IFieldMatcher
    {
        public bool Match(string? value) => value is not null && value.StartsWith(prefix, Cmp);
    }

    private sealed class EndsWithMatcher(string suffix) : IFieldMatcher
    {
        public bool Match(string? value) => value is not null && value.EndsWith(suffix, Cmp);
    }

    private sealed class RegexMatcher : IFieldMatcher
    {
        private readonly Regex _re;
        public RegexMatcher(string pattern)
        {
            _re = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
        }
        public bool Match(string? value) => value is not null && _re.IsMatch(value);
    }

    private sealed class AnyOfMatcher(IFieldMatcher[] inner) : IFieldMatcher
    {
        public bool Match(string? value) => inner.Any(m => m.Match(value));
    }

    private sealed class AllMatcher(IFieldMatcher[] inner) : IFieldMatcher
    {
        public bool Match(string? value) => inner.All(m => m.Match(value));
    }
}
