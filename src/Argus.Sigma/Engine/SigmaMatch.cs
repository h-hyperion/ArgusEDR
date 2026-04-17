using Argus.Sigma.Matching;
using Argus.Sigma.Models;

namespace Argus.Sigma.Engine;

public sealed record SigmaMatch(
    SigmaRule Rule,
    SigmaEventContext Event,
    DateTimeOffset MatchedAtUtc);
