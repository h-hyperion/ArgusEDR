using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Argus.GUI.ViewModels;

public sealed partial class QuarantineViewModel : ObservableObject
{
    [ObservableProperty] private int _itemCount;
    [ObservableProperty] private string _statusMessage = "No files in quarantine";

    [RelayCommand]
    private void ReleaseSelected()
    {
        StatusMessage = "Release requires Watchdog service — connect to proceed";
    }

    [RelayCommand]
    private void DeletePermanently()
    {
        StatusMessage = "Delete requires Watchdog service — connect to proceed";
    }
}
