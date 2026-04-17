using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Argus.GUI.ViewModels;

public sealed partial class ScannerViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    private bool _isScanning;

    [ObservableProperty] private string _status = "Ready";
    [ObservableProperty] private int _scanProgress;

    [ObservableProperty] private bool _scanUserFiles = true;
    [ObservableProperty] private bool _scanSystemFiles;
    [ObservableProperty] private bool _scanRegistry;
    [ObservableProperty] private bool _heuristicAnalysis;

    public ObservableCollection<string> SelectedFiles { get; } = [];

    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task StartScanAsync()
    {
        IsScanning = true;
        ScanProgress = 0;
        Status = "Initializing scan engines...";

        // Simulate scan phases — real implementation uses IPC to Watchdog
        string[] phases = ["YARA engine", "AMSI integration", "Heuristic analysis", "Registry scan"];
        for (int i = 0; i < phases.Length; i++)
        {
            Status = $"Running {phases[i]}...";
            for (int p = 0; p < 25; p++)
            {
                await Task.Delay(40);
                ScanProgress = i * 25 + p;
            }
        }

        ScanProgress = 100;
        IsScanning = false;
        Status = "Scan complete — 0 threats detected";
    }

    private bool CanStartScan() => !IsScanning;

    [RelayCommand]
    private void BrowseFiles()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Select files to scan",
            Filter = "All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
                SelectedFiles.Add(Path.GetFileName(file));

            Status = $"{SelectedFiles.Count} file(s) queued for scanning";
        }
    }

    [RelayCommand]
    private void RemoveFile(string fileName)
    {
        SelectedFiles.Remove(fileName);
        Status = SelectedFiles.Count > 0
            ? $"{SelectedFiles.Count} file(s) queued"
            : "Ready";
    }
}
