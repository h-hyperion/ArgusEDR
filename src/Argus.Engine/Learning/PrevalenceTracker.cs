namespace Argus.Engine.Learning;

/// <summary>
/// Tracks how many times a process has been seen.
/// Low prevalence + suspicious behavior = elevated risk.
/// </summary>
public sealed class PrevalenceTracker
{
    private readonly SignatureDatabase _db;
    private const int RareThreshold = 10; // fewer than 10 sightings = rare

    public PrevalenceTracker(SignatureDatabase db) => _db = db;

    public void RecordSighting(string processName, string imagePath) =>
        _db.IncrementSighting(processName, imagePath);

    public int GetSightingCount(string processName) =>
        _db.GetSightingCount(processName);

    public bool IsRare(string processName) =>
        _db.GetSightingCount(processName) < RareThreshold;

    /// <summary>
    /// Combined prevalence + similarity risk score (0-100, higher = more suspicious).
    /// </summary>
    public int ComputeRiskScore(BehaviorProfile observed, BehaviorProfile? baseline,
        SimilarityEngine similarity)
    {
        int prevalenceScore = IsRare(observed.ProcessName) ? 40 : 0;
        int similarityScore = baseline is null
            ? 30  // No baseline = moderate suspicion
            : (int)((100 - similarity.Score(observed, baseline)) * 0.6);

        return Math.Min(100, prevalenceScore + similarityScore);
    }
}
