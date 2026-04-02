using Serilog;

namespace Argus.Defender.Guard;

public sealed class GuardEnforcer
{
    private readonly IWindowsPrivacyApi _api;

    public GuardEnforcer(IWindowsPrivacyApi api) => _api = api;

    public void ApplyAll(GuardConfig config)
    {
        foreach (var toggle in config.Toggles)
        {
            try { Apply(toggle); }
            catch (Exception ex) { Log.Error(ex, "Failed to apply toggle {Id}", toggle.Id); }
        }
    }

    public void Apply(GuardToggle toggle)
    {
        if (!toggle.Enabled) return;
        Action action = toggle.Id switch
        {
            "telemetry_diagtrack"        => ApplyDiagTrack,
            "telemetry_diagnostic_data"  => ApplyDiagnosticData,
            "telemetry_app_usage"        => ApplyAppUsage,
            "telemetry_activity_history" => ApplyActivityHistory,
            "telemetry_inking_typing"    => ApplyInkingTyping,
            "telemetry_speech"           => ApplySpeech,
            "telemetry_location"         => ApplyLocation,
            "telemetry_wer"              => ApplyWer,
            "telemetry_feedback_freq"    => ApplyFeedback,
            "telemetry_compat_appraiser" => ApplyCompatAppraiser,
            "cloud_onedrive"             => ApplyOneDrive,
            "cloud_cortana"              => ApplyCortana,
            "cloud_clipboard"            => ApplyCloudClipboard,
            "cloud_spotlight"            => ApplySpotlight,
            "cloud_connected_experiences"=> ApplyConnectedExperiences,
            "ads_advertising_id"         => ApplyAdvertisingId,
            "ads_tailored_experiences"   => ApplyTailoredExperiences,
            "ads_start_suggestions"      => ApplyStartSuggestions,
            "ads_notification_ads"       => ApplyNotificationAds,
            "network_delivery_opt"       => ApplyDeliveryOptimization,
            "network_ncsi"               => ApplyNcsi,
            "network_wifi_sense"         => ApplyWifiSense,
            "diag_sample_submission"     => ApplySampleSubmission,
            "diag_app_telemetry"         => ApplyAppTelemetry,
            "diag_device_census"         => ApplyDeviceCensus,
            "diag_scheduled_tasks"       => ApplyDiagnosticTasks,
            _ => () => Log.Warning("Unknown toggle: {Id}", toggle.Id)
        };
        action();
        Log.Information("Applied privacy toggle: {Id}", toggle.Id);
    }

    // --- Telemetry ---
    private void ApplyDiagTrack() => _api.StopAndDisableService("DiagTrack");
    private void ApplyDiagnosticData() =>
        _api.SetRegistryValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0);
    private void ApplyAppUsage() =>
        _api.SetRegistryValue(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs", 0);
    private void ApplyActivityHistory()
    {
        _api.SetRegistryValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0);
        _api.SetRegistryValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0);
        _api.SetRegistryValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0);
    }
    private void ApplyInkingTyping() =>
        _api.SetRegistryValue(@"HKCU\SOFTWARE\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1);
    private void ApplySpeech() =>
        _api.SetRegistryValue(@"HKCU\SOFTWARE\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", 0);
    private void ApplyLocation() =>
        _api.SetRegistryValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1);
    private void ApplyWer() =>
        _api.SetRegistryValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled", 1);
    private void ApplyFeedback() =>
        _api.SetRegistryValue(@"HKCU\SOFTWARE\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0);
    private void ApplyCompatAppraiser() =>
        _api.DisableScheduledTask(@"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser");

    // --- Cloud ---
    private void ApplyOneDrive() =>
        _api.SetRegistryValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC", 1);
    private void ApplyCortana() =>
        _api.SetRegistryValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0);
    private void ApplyCloudClipboard() =>
        _api.SetRegistryValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "AllowCrossDeviceClipboard", 0);
    private void ApplySpotlight() =>
        _api.SetRegistryValue(@"HKCU\SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsSpotlightFeatures", 1);
    private void ApplyConnectedExperiences() =>
        _api.SetRegistryValue(@"HKLM\SOFTWARE\Policies\Microsoft\Office\16.0\Common\Privacy", "DisableConnectedExperiences", 1);

    // --- Advertising ---
    private void ApplyAdvertisingId() =>
        _api.SetRegistryValue(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0);
    private void ApplyTailoredExperiences() =>
        _api.SetRegistryValue(@"HKCU\SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableTailoredExperiencesWithDiagnosticData", 1);
    private void ApplyStartSuggestions() =>
        _api.SetRegistryValue(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0);
    private void ApplyNotificationAds() =>
        _api.SetRegistryValue(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", 0);

    // --- Network ---
    private void ApplyDeliveryOptimization() =>
        _api.SetRegistryValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0);
    private void ApplyNcsi() =>
        _api.SetRegistryValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\NetworkConnectivityStatusIndicator", "NoActiveProbe", 1);
    private void ApplyWifiSense() =>
        _api.SetRegistryValue(@"HKLM\SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config", "AutoConnectAllowedOEM", 0);

    // --- Diagnostics ---
    private void ApplySampleSubmission() =>
        _api.SetRegistryValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Spynet", "SubmitSamplesConsent", 2);
    private void ApplyAppTelemetry() =>
        _api.DisableScheduledTask(@"\Microsoft\Windows\Application Experience\AitAgent");
    private void ApplyDeviceCensus() =>
        _api.DisableScheduledTask(@"\Microsoft\Windows\Device Information\Device");
    private void ApplyDiagnosticTasks() =>
        _api.DisableScheduledTask(@"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector");

    /// <summary>
    /// Returns all registry key paths that a given toggle modifies.
    /// Used by GuardMonitor to build its watch list.
    /// </summary>
    public static IEnumerable<string> GetRegistryKeysForToggle(string toggleId)
    {
        return toggleId switch
        {
            "telemetry_diagnostic_data" => new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection" },
            "telemetry_app_usage" => new[] { @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced" },
            "telemetry_activity_history" => new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System" },
            "telemetry_inking_typing" => new[] { @"HKCU\SOFTWARE\Microsoft\InputPersonalization" },
            "telemetry_speech" => new[] { @"HKCU\SOFTWARE\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy" },
            "telemetry_location" => new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors" },
            "telemetry_wer" => new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting" },
            "telemetry_feedback_freq" => new[] { @"HKCU\SOFTWARE\Microsoft\Siuf\Rules" },
            "cloud_onedrive" => new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\OneDrive" },
            "cloud_cortana" => new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search" },
            "cloud_clipboard" => new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System" },
            "cloud_spotlight" => new[] { @"HKCU\SOFTWARE\Policies\Microsoft\Windows\CloudContent" },
            "cloud_connected_experiences" => new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Office\16.0\Common\Privacy" },
            "ads_advertising_id" => new[] { @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo" },
            "ads_tailored_experiences" => new[] { @"HKCU\SOFTWARE\Policies\Microsoft\Windows\CloudContent" },
            "ads_start_suggestions" => new[] { @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager" },
            "ads_notification_ads" => new[] { @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager" },
            "network_delivery_opt" => new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization" },
            "network_ncsi" => new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\NetworkConnectivityStatusIndicator" },
            "network_wifi_sense" => new[] { @"HKLM\SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config" },
            "diag_sample_submission" => new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Spynet" },
            _ => Array.Empty<string>()
        };
    }
}
