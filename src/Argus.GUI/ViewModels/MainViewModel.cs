using Argus.GUI.IPC;
using Argus.GUI.Views;
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
    [ObservableProperty] private string _repairStatus = string.Empty;

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
            RepairStatus = "Locating installer...";

            var scriptPath = ResolveInstallerScript();
            if (scriptPath is null)
            {
                RepairStatus = "Installer script not found. Re-run the install one-liner.";
                Serilog.Log.Warning("Repair: could not locate argus.ps1 in any known location");
                return;
            }

            RepairStatus = "Launching repair (elevated)...";

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -Repair",
                UseShellExecute = true,
                Verb = "runas",
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                RepairStatus = "Failed to start elevated PowerShell.";
                return;
            }

            await process.WaitForExitAsync();
            if (process.ExitCode == 0)
            {
                IsSafeMode = false;
                SafeModeReason = string.Empty;
                RepairStatus = "Repair complete.";
            }
            else
            {
                RepairStatus = $"Repair script exited with code {process.ExitCode}.";
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User dismissed the UAC prompt.
            RepairStatus = "Repair cancelled (UAC declined).";
        }
        catch (Exception ex)
        {
            RepairStatus = $"Repair failed: {ex.Message}";
            Serilog.Log.Error(ex, "Failed to run repair script");
        }
    }

    private static string? ResolveInstallerScript()
    {
        // Installer copies argus.ps1 into <InstallDir>\installer\ during first install.
        // Deployed layout per argus.ps1: $INSTALL_DIR = %ProgramFiles%\Argus.
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Argus", "installer", "argus.ps1"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Argus", "installer", "install", "argus.ps1"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Argus", "installer", "install", "argus.ps1"),
            // Dev fallback: repo checkout next to the running exe.
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "installer", "install", "argus.ps1"),
        };

        foreach (var c in candidates)
        {
            try
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(full)) return full;
            }
            catch { /* path construction errors are non-fatal */ }
        }
        return null;
    }

    [RelayCommand] private void ShowDailyBriefing()
    {
        DailyBriefingDialog.Show(Dashboard);
    }

    public void ActivateSafeMode(string reason)
    {
        IsSafeMode = true;
        SafeModeReason = reason;
    }
}
