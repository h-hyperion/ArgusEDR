using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Argus.GUI.ViewModels;

namespace Argus.GUI.Views;

/// <summary>
/// Minimal WPF dialog summarizing today's protection state from the
/// <see cref="DashboardViewModel"/>. Code-behind (no XAML) to match the
/// one-off dialog pattern already in use by <see cref="ApiKeyPrompt"/>.
/// </summary>
internal static class DailyBriefingDialog
{
    public static void Show(DashboardViewModel dashboard)
    {
        var window = new Window
        {
            Title = "Argus EDR — Daily Briefing",
            Width = 520,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)(Application.Current.Resources["BgSurface"]
                                 ?? new SolidColorBrush(Color.FromRgb(0x14, 0x0B, 0x0B))),
        };

        var header = new TextBlock
        {
            Text = "— Daily Briefing —",
            Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xAF, 0x4B)),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
        };

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(header);

        AddRow(panel, "Protection status",  dashboard.ProtectionStatus);
        AddRow(panel, "Active monitors",    dashboard.ActiveMonitors);
        AddRow(panel, "Watchdog",           dashboard.WatchdogStatus);
        AddRow(panel, "Defender",           dashboard.DefenderStatus);
        AddRow(panel, "Threats blocked",    dashboard.ThreatsDetected.ToString());
        AddRow(panel, "Files scanned",      dashboard.FilesScanned.ToString());
        AddRow(panel, "Quarantined items",  dashboard.QuarantinedItems.ToString());
        AddRow(panel, "Last scan",          dashboard.LastScanTime);

        var close = new Button
        {
            Content = "Close",
            Width = 110,
            Height = 32,
            Margin = new Thickness(0, 20, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsCancel = true,
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(0x8B, 0x6F, 0x2A)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x0A, 0x03, 0x03)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD4, 0xAF, 0x4B)),
            BorderThickness = new Thickness(1),
            FontWeight = FontWeights.SemiBold,
        };
        panel.Children.Add(close);

        window.Content = panel;
        window.ShowDialog();
    }

    private static void AddRow(Panel parent, string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var l = new TextBlock
        {
            Text = label,
            Foreground = Brushes.Gainsboro,
            FontSize = 13,
            Opacity = 0.75,
        };
        Grid.SetColumn(l, 0);

        var v = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC5, 0x78)),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(v, 1);

        grid.Children.Add(l);
        grid.Children.Add(v);
        parent.Children.Add(grid);
    }
}
