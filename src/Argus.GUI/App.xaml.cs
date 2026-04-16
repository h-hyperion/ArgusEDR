using System.IO;
using System.Windows;
using Argus.Defender.Dns;
using Argus.Defender.Guard;
using Argus.GUI.IPC;
using Argus.GUI.Notifications;
using Argus.GUI.Tray;
using Argus.GUI.ViewModels;
using Microsoft.Toolkit.Uwp.Notifications;
using Serilog;

namespace Argus.GUI;

public partial class App : Application
{
    private GuiPipeBridge? _pipeBridge;
    private DefenderPipeBridge? _defenderBridge;
    private TrayIconManager? _trayIcon;
    private NotificationService? _notifications;
    private bool _startMinimized;
    private string? _scanPath;
    private bool _isExiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ParseArguments(e.Args);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Argus", "Logs", "gui-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        // Listen for toast notification activations
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;

        _pipeBridge = new GuiPipeBridge();
        _defenderBridge = new DefenderPipeBridge();

        var dnsService = new DnsProtectionService(new WindowsDnsNativeApi());
        var guardEnforcer = new GuardEnforcer(new WindowsPrivacyApi());

        var dashboard = new DashboardViewModel(_pipeBridge, _defenderBridge);
        var scanner = new ScannerViewModel();
        var defender = new DefenderViewModel(_defenderBridge);
        var privacyGuard = new PrivacyGuardViewModel(guardEnforcer);
        var quarantine = new QuarantineViewModel();
        var settings = new SettingsViewModel(dnsService);

        var mainVm = new MainViewModel(
            _pipeBridge, dashboard, privacyGuard, settings,
            scanner, defender, quarantine);

        _pipeBridge.SafeModeTriggered += reason => Dispatcher.Invoke(() =>
            mainVm.ActivateSafeMode(reason));

        var window = new MainWindow { DataContext = mainVm };

        // Set up system tray
        _trayIcon = new TrayIconManager(_pipeBridge, window);
        _trayIcon.QuickScanRequested += () => Dispatcher.Invoke(() =>
        {
            _trayIcon.ShowMainWindow();
            mainVm.ShowScannerCommand.Execute(null);
        });
        _trayIcon.ExitRequested += () => Dispatcher.Invoke(() =>
        {
            _isExiting = true;
            Shutdown();
        });

        // Set up notifications
        _notifications = new NotificationService(_pipeBridge);

        // Show window unless --minimized
        if (_startMinimized)
        {
            // Window is created but not shown — tray icon is the only visible element
            Log.Information("Starting minimized to system tray");
        }
        else
        {
            window.Show();
        }

        // Handle --scan argument
        if (_scanPath != null)
        {
            window.Show();
            mainVm.ShowScannerCommand.Execute(null);
            Log.Information("Scan requested for: {Path}", _scanPath);
        }

        try
        {
            await _pipeBridge.StartAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to connect to Watchdog service on startup");
        }
    }

    private void ParseArguments(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--minimized":
                    _startMinimized = true;
                    break;
                case "--scan" when i + 1 < args.Length:
                    _scanPath = args[++i];
                    break;
            }
        }
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        Dispatcher.Invoke(() =>
        {
            _trayIcon?.ShowMainWindow();
        });
    }

    /// <summary>
    /// Called by MainWindow.OnClosing to determine if the app is truly exiting
    /// or if the window should hide to tray instead.
    /// </summary>
    public bool IsExiting => _isExiting;

    protected override void OnExit(ExitEventArgs e)
    {
        _notifications?.Dispose();
        _trayIcon?.Dispose();
        _defenderBridge?.Dispose();
        _pipeBridge?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
