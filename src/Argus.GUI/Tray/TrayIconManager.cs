using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Controls;
using Argus.Core;
using Argus.GUI.IPC;
using Hardcodet.Wpf.TaskbarNotification;
using Serilog;

namespace Argus.GUI.Tray;

/// <summary>
/// Manages the system tray icon lifecycle, color-coded status,
/// and right-click context menu for Argus EDR.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly TaskbarIcon _trayIcon;
    private readonly GuiPipeBridge _pipeBridge;
    private readonly Window _mainWindow;

    private Icon _greenIcon;
    private Icon _goldIcon;
    private Icon _redIcon;
    private Icon _grayIcon;
    private TrayState _currentState = TrayState.Disconnected;

    public enum TrayState { Protected, Attention, Threat, Disconnected }

    public event Action? QuickScanRequested;
    public event Action? ExitRequested;

    public TrayIconManager(GuiPipeBridge pipeBridge, Window mainWindow)
    {
        _pipeBridge = pipeBridge;
        _mainWindow = mainWindow;

        _greenIcon = GenerateShieldIcon(Color.FromArgb(74, 222, 128));
        _goldIcon = GenerateShieldIcon(Color.FromArgb(201, 168, 76));
        _redIcon = GenerateShieldIcon(Color.FromArgb(239, 68, 68));
        _grayIcon = GenerateShieldIcon(Color.FromArgb(128, 128, 128));

        _trayIcon = new TaskbarIcon
        {
            Icon = _grayIcon,
            ToolTipText = "Argus EDR — Connecting...",
            ContextMenu = BuildContextMenu(),
            MenuActivation = PopupActivationMode.RightClick
        };

        _trayIcon.TrayMouseDoubleClick += OnTrayDoubleClick;
        _pipeBridge.StatusUpdated += OnStatusUpdated;

        UpdateState(TrayState.Disconnected);
    }

    public void UpdateState(TrayState state)
    {
        _currentState = state;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            switch (state)
            {
                case TrayState.Protected:
                    _trayIcon.Icon = _greenIcon;
                    _trayIcon.ToolTipText = "Argus EDR — Protection Active";
                    break;
                case TrayState.Attention:
                    _trayIcon.Icon = _goldIcon;
                    _trayIcon.ToolTipText = "Argus EDR — Attention Required";
                    break;
                case TrayState.Threat:
                    _trayIcon.Icon = _redIcon;
                    _trayIcon.ToolTipText = "Argus EDR — Threat Detected";
                    break;
                case TrayState.Disconnected:
                    _trayIcon.Icon = _grayIcon;
                    _trayIcon.ToolTipText = "Argus EDR — Service Disconnected";
                    break;
            }
        });
    }

    private void OnStatusUpdated(ServiceStatus status)
    {
        if (status.SafeModeActive)
            UpdateState(TrayState.Threat);
        else if (!status.ServiceRunning)
            UpdateState(TrayState.Attention);
        else
            UpdateState(TrayState.Protected);
    }

    private void OnTrayDoubleClick(object sender, RoutedEventArgs e)
    {
        ShowMainWindow();
    }

    public void ShowMainWindow()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        });
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var openItem = new MenuItem { Header = "Open Argus EDR", FontWeight = FontWeights.Bold };
        openItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(openItem);

        var scanItem = new MenuItem { Header = "Quick Scan" };
        scanItem.Click += (_, _) => QuickScanRequested?.Invoke();
        menu.Items.Add(scanItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "Exit Argus" };
        exitItem.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(exitItem);

        return menu;
    }

    private static Icon GenerateShieldIcon(Color fillColor, int size = 32)
    {
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        float m = size / 16f;
        var points = new PointF[]
        {
            new(8 * m, 1 * m),
            new(14 * m, 3 * m),
            new(14 * m, 8 * m),
            new(8 * m, 15 * m),
            new(2 * m, 8 * m),
            new(2 * m, 3 * m)
        };

        using var brush = new SolidBrush(fillColor);
        g.FillPolygon(brush, points);

        using var pen = new Pen(Color.FromArgb(80, 0, 0, 0), 1f * m);
        g.DrawPolygon(pen, points);

        var handle = bmp.GetHicon();
        return Icon.FromHandle(handle);
    }

    public void Dispose()
    {
        _pipeBridge.StatusUpdated -= OnStatusUpdated;
        _trayIcon.TrayMouseDoubleClick -= OnTrayDoubleClick;
        _trayIcon.Dispose();
        _greenIcon.Dispose();
        _goldIcon.Dispose();
        _redIcon.Dispose();
        _grayIcon.Dispose();
    }
}
