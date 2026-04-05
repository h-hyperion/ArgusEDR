namespace Argus.Defender.Dns;

public sealed record DnsProfile(
    string Name,
    string Primary,
    string Secondary,
    string Description)
{
    public static readonly DnsProfile Privacy = new(
        "Privacy",
        "1.1.1.1",
        "1.0.0.1",
        "Cloudflare privacy-focused DNS - no query logging, fastest resolver");

    public static readonly DnsProfile MalwareBlocking = new(
        "Malware Blocking",
        "1.1.1.2",
        "1.0.0.2",
        "Cloudflare DNS with malware domain blocking");

    public static readonly DnsProfile Family = new(
        "Family",
        "1.1.1.3",
        "1.0.0.3",
        "Cloudflare DNS with malware and adult content blocking");

    public static readonly DnsProfile System = new(
        "System Default",
        string.Empty,
        string.Empty,
        "Use system/router assigned DNS");

    public static IReadOnlyList<DnsProfile> All =>
        new[] { Privacy, MalwareBlocking, Family, System };
}