using Argus.Defender.Dns;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Serilog;

namespace Argus.GUI.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly DnsProtectionService _dns;

    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ArgusEDR";
    private static readonly string ApiKeysPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                     "Argus", "Config", "ApiKeys.json");

    public ObservableCollection<DnsProfile> DnsProfiles { get; } =
        new(DnsProfile.All);

    [ObservableProperty] private DnsProfile _selectedDnsProfile;
    [ObservableProperty] private string _statusMessage = "Select a DNS profile and click Apply.";
    [ObservableProperty] private bool _launchAtStartup;
    [ObservableProperty] private string _virusTotalKeyStatus = "VirusTotal — Not configured";
    [ObservableProperty] private string _abuseIpdbKeyStatus = "AbuseIPDB — Not configured";
    [ObservableProperty] private string _apiKeyStatusMessage = "";

    private void LoadApiKeyStatus()
    {
        try
        {
            if (!File.Exists(ApiKeysPath)) return;
            var json = File.ReadAllText(ApiKeysPath);
            var keys = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            if (keys.TryGetValue("VirusTotal", out var vt) && !string.IsNullOrWhiteSpace(vt))
                VirusTotalKeyStatus = $"VirusTotal — Configured (\u2022\u2022\u2022{vt[^Math.Min(4, vt.Length)..]})";
            if (keys.TryGetValue("AbuseIPDB", out var ab) && !string.IsNullOrWhiteSpace(ab))
                AbuseIpdbKeyStatus = $"AbuseIPDB — Configured (\u2022\u2022\u2022{ab[^Math.Min(4, ab.Length)..]})";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read API keys");
        }
    }

    public SettingsViewModel(DnsProtectionService dns)
    {
        _dns = dns;
        _selectedDnsProfile = dns.CurrentProfile;
        _launchAtStartup = ReadStartupRegistryValue();
        LoadApiKeyStatus();
    }

    [RelayCommand]
    private void AddApiKey(string service)
    {
        if (string.IsNullOrWhiteSpace(service)) return;
        var key = Argus.GUI.Views.ApiKeyPrompt.Prompt(service, ApiKeysPath);
        if (key is null) return; // cancelled
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ApiKeysPath)!);
            var keys = File.Exists(ApiKeysPath)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ApiKeysPath)) ?? new()
                : new Dictionary<string, string>();

            if (string.IsNullOrWhiteSpace(key))
            {
                keys.Remove(service);
                ApiKeyStatusMessage = $"{service} key removed.";
            }
            else
            {
                keys[service] = key.Trim();
                ApiKeyStatusMessage = $"{service} key saved.";
            }

            File.WriteAllText(ApiKeysPath,
                JsonSerializer.Serialize(keys, new JsonSerializerOptions { WriteIndented = true }));
            LoadApiKeyStatus();
        }
        catch (Exception ex)
        {
            ApiKeyStatusMessage = $"Failed to save key: {ex.Message}";
            Log.Error(ex, "Failed to save API key for {Service}", service);
        }
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