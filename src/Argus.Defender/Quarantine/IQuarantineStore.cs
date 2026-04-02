using Argus.Core.Models;

namespace Argus.Defender.Quarantine;

public interface IQuarantineStore
{
    Task QuarantineAsync(string filePath, ThreatResult threat, CancellationToken ct = default);
    Task<string> RestoreAsync(string entryId, string restorePath, CancellationToken ct = default);
    IReadOnlyList<QuarantineEntry> List();
}
