using Argus.Core.Models;

namespace Argus.Defender.Quarantine;

public sealed record QuarantineEntry
{
    public required string Id { get; init; }
    public required string OriginalPath { get; init; }
    public required string EncryptedPath { get; init; }
    public required ThreatLevel ThreatLevel { get; init; }
    public required string Evidence { get; init; }
    public required DateTimeOffset QuarantinedAt { get; init; }
    public long OriginalSize { get; init; }
    public string? OriginalHash { get; init; }
}
