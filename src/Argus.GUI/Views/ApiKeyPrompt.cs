using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Argus.GUI.Views;

/// <summary>
/// Minimal WPF input dialog for entering API keys. Implemented in code-behind
/// (no XAML) so we don't need to wire up a BAML resource for a one-off prompt.
/// Returns the entered text, or null if the user cancelled.
/// </summary>
internal static class ApiKeyPrompt
{
    public static string? Prompt(string service, string storagePath)
    {
        var window = new Window
        {
            Title = $"Argus EDR — {service} API Key",
            Width = 480,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)(Application.Current.Resources["BgSurface"]
                                 ?? new SolidColorBrush(Color.FromRgb(0x14, 0x0B, 0x0B))),
        };

        var prompt = new TextBlock
        {
            Text = $"Enter your {service} API key.\nStored locally at:\n{storagePath}\n\nLeave blank to remove an existing key.",
            Foreground = Brushes.Gainsboro,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };

        var input = new TextBox
        {
            FontSize = 13,
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x07, 0x07)),
            Foreground = Brushes.Gainsboro,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD4, 0xAF, 0x4B)),
            BorderThickness = new Thickness(1),
        };

        var ok = new Button
        {
            Content = "Save",
            Width = 90,
            Height = 30,
            Margin = new Thickness(6, 12, 0, 0),
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(0x8B, 0x6F, 0x2A)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x0A, 0x03, 0x03)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD4, 0xAF, 0x4B)),
            BorderThickness = new Thickness(1),
            FontWeight = FontWeights.SemiBold,
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Width = 90,
            Height = 30,
            Margin = new Thickness(6, 12, 0, 0),
            IsCancel = true,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xAF, 0x4B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD4, 0xAF, 0x4B)),
            BorderThickness = new Thickness(1),
        };

        string? result = null;
        ok.Click += (_, _) => { result = input.Text ?? ""; window.DialogResult = true; };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(prompt);
        panel.Children.Add(input);
        panel.Children.Add(buttons);
        window.Content = panel;

        input.Focus();
        return window.ShowDialog() == true ? result : null;
    }
}
