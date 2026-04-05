using Microsoft.Win32;
using Serilog;
using System.ServiceProcess;

namespace Argus.Defender.Guard;

/// <summary>
/// Production implementation of IWindowsPrivacyApi.
/// Modifies the Windows registry, services, and scheduled tasks.
/// Requires elevated privileges for HKLM keys and service operations.
/// </summary>
public sealed class WindowsPrivacyApi : IWindowsPrivacyApi
{
    public void SetRegistryValue(string keyPath, string valueName, object value)
    {
        // Parse "HKLM\SOFTWARE\..." into root key + subpath
        var (root, subPath) = ParseKeyPath(keyPath);
        using var key = root.CreateSubKey(subPath, writable: true)
            ?? throw new InvalidOperationException($"Cannot open registry key: {keyPath}");
        key.SetValue(valueName, value, value is string ? RegistryValueKind.String : RegistryValueKind.DWord);
        Log.Debug("Registry set: {Key}\\{Value} = {Data}", keyPath, valueName, value);
    }

    public void DeleteRegistryValue(string keyPath, string valueName)
    {
        var (root, subPath) = ParseKeyPath(keyPath);
        using var key = root.OpenSubKey(subPath, writable: true);
        if (key is null) return;
        key.DeleteValue(valueName, throwOnMissingValue: false);
        Log.Debug("Registry deleted: {Key}\\{Value}", keyPath, valueName);
    }

    public void StopAndDisableService(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status == ServiceControllerStatus.Running)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            }
            // Disable via registry (sc.exe equivalent)
            SetRegistryValue(
                $@"HKLM\SYSTEM\CurrentControlSet\Services\{serviceName}",
                "Start", 4); // 4 = Disabled
            Log.Information("Service stopped and disabled: {Service}", serviceName);
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "Service not found or cannot be stopped: {Service}", serviceName);
        }
    }

    public void DisableScheduledTask(string taskPath)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Change /TN \"{taskPath}\" /Disable",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(10_000);
            Log.Information("Scheduled task disabled: {Task}", taskPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to disable scheduled task: {Task}", taskPath);
        }
    }

    private static (RegistryKey root, string subPath) ParseKeyPath(string keyPath)
    {
        var firstSlash = keyPath.IndexOf('\\');
        if (firstSlash < 0) throw new ArgumentException($"Invalid registry path: {keyPath}");

        var rootName = keyPath[..firstSlash].ToUpperInvariant();
        var subPath = keyPath[(firstSlash + 1)..];

        RegistryKey root = rootName switch
        {
            "HKLM" => Registry.LocalMachine,
            "HKCU" => Registry.CurrentUser,
            "HKCR" => Registry.ClassesRoot,
            _ => throw new ArgumentException($"Unknown registry root: {rootName}")
        };
        return (root, subPath);
    }
}
