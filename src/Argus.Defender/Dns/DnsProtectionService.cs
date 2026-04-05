using Serilog;

namespace Argus.Defender.Dns;

public sealed class DnsProtectionService
{
    private readonly IDnsNativeApi _api;
    public DnsProfile CurrentProfile { get; private set; } = DnsProfile.System;

    public DnsProtectionService(IDnsNativeApi api)
    {
        _api = api;
    }

    public void Apply(DnsProfile profile)
    {
        CurrentProfile = profile;
        var adapters = _api.GetNetworkAdapterNames();

        foreach (var adapter in adapters)
        {
            try
            {
                if (string.IsNullOrEmpty(profile.Primary))
                {
                    _api.ResetDnsToAutomatic(adapter);
                    Log.Information("Reset DNS to automatic for adapter: {Adapter}", adapter);
                }
                else
                {
                    _api.SetDnsServers(adapter, profile.Primary, profile.Secondary);
                    Log.Information("Set DNS to {Primary}/{Secondary} for adapter: {Adapter}",
                        profile.Primary, profile.Secondary, adapter);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to apply DNS settings for adapter: {Adapter}", adapter);
            }
        }
    }
}