using System.Management;

namespace Argus.Defender.Dns;

public sealed class WindowsDnsNativeApi : IDnsNativeApi
{
    public IReadOnlyList<string> GetNetworkAdapterNames()
    {
        var adapters = new List<string>();
        try
        {
            using var query = new ManagementObjectSearcher(
                "SELECT Description FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=True");
            foreach (ManagementObject obj in query.Get())
            {
                var desc = obj["Description"]?.ToString();
                if (!string.IsNullOrEmpty(desc))
                    adapters.Add(desc);
            }
        }
        catch { }
        return adapters;
    }

    public void SetDnsServers(string adapterName, string primary, string secondary)
    {
        try
        {
            using var query = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_NetworkAdapterConfiguration WHERE Description='{adapterName.Replace("'", "''")}'");
            foreach (ManagementObject obj in query.Get())
            {
                var method = obj.GetMethodParameters("SetDNSServerSearchOrder");
                method["DNSServerSearchOrder"] = new[] { primary, secondary };
                obj.InvokeMethod("SetDNSServerSearchOrder", method, null);
            }
        }
        catch { }
    }

    public void ResetDnsToAutomatic(string adapterName)
    {
        try
        {
            using var query = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_NetworkAdapterConfiguration WHERE Description='{adapterName.Replace("'", "''")}'");
            foreach (ManagementObject obj in query.Get())
            {
                var method = obj.GetMethodParameters("SetDNSServerSearchOrder");
                method["DNSServerSearchOrder"] = Array.Empty<string>();
                obj.InvokeMethod("SetDNSServerSearchOrder", method, null);
            }
        }
        catch { }
    }
}