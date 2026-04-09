using System.ComponentModel;
using System.Windows;

namespace Argus.GUI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // If the app is truly exiting (via tray Exit), allow the close
        if (Application.Current is App app && app.IsExiting)
        {
            base.OnClosing(e);
            return;
        }

        // Otherwise, hide to system tray instead of closing
        e.Cancel = true;
        Hide();
    }
}
