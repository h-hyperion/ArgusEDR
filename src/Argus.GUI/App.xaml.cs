using System.IO;
using System.Windows;
using Argus.Defender.Dns;
using Argus.Defender.Guard;
using Argus.GUI.IPC;
using Argus.GUI.ViewModels;
using Serilog;

namespace Argus.GUI;

public partial class App : Application
{
    private GuiPipeBridge? _pipeBridge;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Argus", "Logs", "gui-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        _pipeBridge = new GuiPipeBridge();

        var dnsService = new DnsProtectionService(new WindowsDnsNativeApi());
        var guardEnforcer = new GuardEnforcer(new WindowsPrivacyApi());

        var dashboard = new DashboardViewModel(_pipeBridge);
        var scanner = new ScannerViewModel();
        var defender = new DefenderViewModel();
        var privacyGuard = new PrivacyGuardViewModel(guardEnforcer);
        var quarantine = new QuarantineViewModel();
        var settings = new SettingsViewModel(dnsService);

        var mainVm = new MainViewModel(
            _pipeBridge, dashboard, privacyGuard, settings,
            scanner, defender, quarantine);

        _pipeBridge.SafeModeTriggered += reason => Dispatcher.Invoke(() =>
            mainVm.ActivateSafeMode(reason));

        var window = new MainWindow { DataContext = mainVm };
        window.Show();

        try
        {
            await _pipeBridge.StartAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to connect to Watchdog service on startup");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeBridge?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
