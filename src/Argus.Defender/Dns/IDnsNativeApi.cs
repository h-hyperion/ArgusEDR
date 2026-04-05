namespace Argus.Defender.Dns;

public interface IDnsNativeApi
{
    IReadOnlyList<string> GetNetworkAdapterNames();
    void SetDnsServers(string adapterName, string primary, string secondary);
    void ResetDnsToAutomatic(string adapterName);
}