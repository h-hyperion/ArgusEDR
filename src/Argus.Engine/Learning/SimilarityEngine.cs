namespace Argus.Engine.Learning;

/// <summary>
/// Scores behavioral similarity between an observed process and a known-good baseline.
/// Returns 0-100 where 100 = identical.
/// </summary>
public sealed class SimilarityEngine
{
    public int Score(BehaviorProfile observed, BehaviorProfile baseline)
    {
        double score = 0;
        const double nameWeight    = 0.30;
        const double pathWeight    = 0.40;
        const double moduleWeight  = 0.30;

        score += nameWeight    * FuzzyStringScore(observed.ProcessName, baseline.ProcessName);
        score += pathWeight    * FuzzyStringScore(
            observed.ImagePath.ToLowerInvariant(),
            baseline.ImagePath.ToLowerInvariant());
        score += moduleWeight  * JaccardSimilarity(observed.LoadedModules, baseline.LoadedModules);

        return (int)Math.Round(score);
    }

    private static double FuzzyStringScore(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return 100;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;

        int dist = Levenshtein(a.ToLowerInvariant(), b.ToLowerInvariant());
        int maxLen = Math.Max(a.Length, b.Length);
        return (1.0 - (double)dist / maxLen) * 100;
    }

    private static double JaccardSimilarity(List<string> a, List<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 100;
        var setA = a.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var setB = b.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int intersection = setA.Intersect(setB).Count();
        int union = setA.Union(setB).Count();
        return union == 0 ? 100 : (double)intersection / union * 100;
    }

    private static int Levenshtein(string s, string t)
    {
        int m = s.Length, n = t.Length;
        var d = new int[m + 1, n + 1];
        for (int i = 0; i <= m; i++) d[i, 0] = i;
        for (int j = 0; j <= n; j++) d[0, j] = j;
        for (int i = 1; i <= m; i++)
            for (int j = 1; j <= n; j++)
                d[i, j] = s[i-1] == t[j-1]
                    ? d[i-1, j-1]
                    : 1 + Math.Min(d[i-1, j], Math.Min(d[i, j-1], d[i-1, j-1]));
        return d[m, n];
    }
}
