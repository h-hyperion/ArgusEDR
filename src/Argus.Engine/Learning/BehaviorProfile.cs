namespace Argus.Engine.Learning;

public sealed class BehaviorProfile
{
    public int ProcessId { get; init; }
    public string ImagePath { get; init; } = string.Empty;
    public int ParentProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public List<string> LoadedModules { get; init; } = new();
    public List<string> FilePathsAccessed { get; init; } = new();
    public List<string> RegistryKeysAccessed { get; init; } = new();
    public List<string> NetworkEndpoints { get; init; } = new();
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    // Derived risk signals
    public bool HasSuspiciousImports =>
        LoadedModules.Any(m => SuspiciousModules.Contains(Path.GetFileName(m).ToLowerInvariant()));

    private static readonly HashSet<string> SuspiciousModules =
        new(StringComparer.OrdinalIgnoreCase) { "mimikatz.dll", "minhook.dll" };
}
