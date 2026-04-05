using Microsoft.Win32;
using Serilog;

namespace Argus.Watchdog.SecurityCenter;

public enum WscProtectionStatus { On, Off, Expired }
public enum WscSignatureStatus { UpToDate, OutOfDate }

public sealed record WscPayload(
    string ProductName,
    int ProductState,
    WscSignatureStatus SignatureStatus);

public sealed class WscRegistrar
{
    private const string ArgusGuid = "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}";
    private const string WscAvPath = @"SOFTWARE\Microsoft\Security Center\Provider\Av";

    public WscPayload BuildPayload(string productName, WscProtectionStatus status)
    {
        int rtpBits = status switch
        {
            WscProtectionStatus.On => 0x1000,
            WscProtectionStatus.Expired => 0x1000,
            _ => 0x0000
        };
        int productState = rtpBits | 0x0010;

        var sigStatus = status == WscProtectionStatus.Expired
            ? WscSignatureStatus.OutOfDate
            : WscSignatureStatus.UpToDate;

        return new WscPayload(productName, productState, sigStatus);
    }

    public void Register()
    {
        try
        {
            var payload = BuildPayload("Argus EDR", WscProtectionStatus.On);
            using var key = Registry.LocalMachine.CreateSubKey($@"{WscAvPath}\{ArgusGuid}");
            if (key is null) return;

            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;

            key.SetValue("DisplayName", payload.ProductName, RegistryValueKind.String);
            key.SetValue("ProductState", payload.ProductState, RegistryValueKind.DWord);
            key.SetValue("SignatureStatus", (int)payload.SignatureStatus, RegistryValueKind.DWord);
            key.SetValue("PathToSignedProductExe", exePath, RegistryValueKind.String);
            key.SetValue("PathToSignedReportingExe", exePath, RegistryValueKind.String);

            Log.Information("Registered with Windows Security Center");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to register with Windows Security Center");
        }
    }

    public void Unregister()
    {
        try
        {
            Registry.LocalMachine.DeleteSubKeyTree($@"{WscAvPath}\{ArgusGuid}", throwOnMissingSubKey: false);
            Log.Information("Deregistered from Windows Security Center");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to deregister from Windows Security Center");
        }
    }

    public void UpdateState(WscProtectionStatus status)
    {
        try
        {
            var payload = BuildPayload("Argus EDR", status);
            using var key = Registry.LocalMachine.OpenSubKey($@"{WscAvPath}\{ArgusGuid}", writable: true);
            key?.SetValue("ProductState", payload.ProductState, RegistryValueKind.DWord);
            Log.Information("Updated Windows Security Center state to {Status}", status);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to update Windows Security Center state");
        }
    }
}