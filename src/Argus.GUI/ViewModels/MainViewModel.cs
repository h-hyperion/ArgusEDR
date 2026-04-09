using Argus.GUI.IPC;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.IO;

namespace Argus.GUI.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly GuiPipeBridge _pipeBridge;

    [ObservableProperty] private ObservableObject? _currentView;
    [ObservableProperty] private bool _isSafeMode;
    [ObservableProperty] private string _safeModeReason = string.Empty;

    /// <summary>
    /// Pretty-printed title for the topbar, derived from the current view.
    /// Updated automatically whenever <see cref="CurrentView"/> changes.
    /// </summary>
    public string CurrentPageTitle => CurrentView switch
    {
        DashboardViewModel    => "Dashboard",
        ScannerViewModel      => "Scanner",
        DefenderViewModel     => "Defender",
        PrivacyGuardViewModel => "Privacy Guard",
        QuarantineViewModel   => "Quarantine",
        SettingsViewModel     => "Settings",
        _                     => "Argus EDR"
    };

    partial void OnCurrentViewChanged(ObservableObject? value) =>
        OnPropertyChanged(nameof(CurrentPageTitle));

    // Status bar bindings
    public string StatusBarText => _pipeBridge.StatusMessage;
    public string PipeBridgeStatus => _pipeBridge.PipeConnected
        ? "● Connected | " + _pipeBridge.StatusMessage
        : "○ Disconnected | " + _pipeBridge.StatusMessage;

    public DashboardViewModel Dashboard { get; }
    public PrivacyGuardViewModel PrivacyGuard { get; }
    public SettingsViewModel Settings { get; }
    public ScannerViewModel Scanner { get; }
    public DefenderViewModel Defender { get; }
    public QuarantineViewModel Quarantine { get; }

    public MainViewModel(
        GuiPipeBridge pipeBridge,
        DashboardViewModel dashboard,
        PrivacyGuardViewModel privacyGuard,
        SettingsViewModel settings,
        ScannerViewModel scanner,
        DefenderViewModel defender,
        QuarantineViewModel quarantine)
    {
        _pipeBridge = pipeBridge;
        Dashboard = dashboard;
        PrivacyGuard = privacyGuard;
        Settings = settings;
        Scanner = scanner;
        Defender = defender;
        Quarantine = quarantine;

        _pipeBridge.StatusUpdated += _ => OnPropertyChanged(nameof(StatusBarText));
        _pipeBridge.StatusUpdated += _ => OnPropertyChanged(nameof(PipeBridgeStatus));

        CurrentView = Dashboard;
    }

    [RelayCommand] private void ShowDashboard()    => CurrentView = Dashboard;
    [RelayCommand] private void ShowScanner()      => CurrentView = Scanner;
    [RelayCommand] private void ShowDefender()     => CurrentView = Defender;
    [RelayCommand] private void ShowPrivacyGuard() => CurrentView = PrivacyGuard;
    [RelayCommand] private void ShowQuarantine()   => CurrentView = Quarantine;
    [RelayCommand] private void ShowSettings()     => CurrentView = Settings;

    [RelayCommand] private async Task RepairArgus()
    {
        try
        {
            var scriptPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Argus", "installer", "install", "argus.ps1");

            if (!File.Exists(scriptPath))
                scriptPath = @"C:\ProgramData\Argus\installer\install\argus.ps1";

            if (!File.Exists(scriptPath))
                throw new FileNotFoundException("Installer script not found at any known location.");

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -Repair",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                {
                    IsSafeMode = false;
                    SafeModeReason = string.Empty;
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to run repair script");
        }
    }

    public void ActivateSafeMode(string reason)
    {
        IsSafeMode = true;
        SafeModeReason = reason;
    }
}
