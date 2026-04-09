using Argus.Defender.Dns;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using Serilog;

namespace Argus.GUI.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly DnsProtectionService _dns;

    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ArgusEDR";

    public ObservableCollection<DnsProfile> DnsProfiles { get; } =
        new(DnsProfile.All);

    [ObservableProperty] private DnsProfile _selectedDnsProfile;
    [ObservableProperty] private string _statusMessage = "Select a DNS profile and click Apply.";
    [ObservableProperty] private bool _launchAtStartup;

    public SettingsViewModel(DnsProtectionService dns)
    {
        _dns = dns;
        _selectedDnsProfile = dns.CurrentProfile;
        _launchAtStartup = ReadStartupRegistryValue();
    }

    partial void OnLaunchAtStartupChanged(bool value)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                Log.Warning("Cannot open Run registry key for writing");
                return;
            }

            if (value)
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (exePath != null)
                    key.SetValue(RunValueName, $"\"{exePath}\" --minimized");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to update startup registry key");
            StatusMessage = "Could not update startup setting (requires admin).";
        }
    }

    private static bool ReadStartupRegistryValue()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(RunValueName) != null;
        }
        catch
        {
            return false;
        }
    }

    [RelayCommand]
    private void ApplyDns()
    {
        try
        {
            _dns.Apply(SelectedDnsProfile);
            StatusMessage = SelectedDnsProfile == DnsProfile.System
                ? "DNS reset to system default."
                : $"DNS set to {SelectedDnsProfile.Name} ({SelectedDnsProfile.Primary}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to apply DNS settings: {ex.Message}";
            Serilog.Log.Error(ex, "Failed to apply DNS settings");
        }
    }
}