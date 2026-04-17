using Argus.Defender.Guard;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace Argus.GUI.ViewModels;

public sealed partial class PrivacyGuardViewModel : ObservableObject
{
    private readonly GuardEnforcer _enforcer;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private int _changesCount;
    [ObservableProperty] private int _activeGuards;

    public ObservableCollection<GuardToggle> Toggles { get; } = new();

    public IEnumerable<IGrouping<string, GuardToggle>> GroupedToggles =>
        Toggles.GroupBy(t => t.Category);

    public PrivacyGuardViewModel(GuardEnforcer enforcer)
    {
        _enforcer = enforcer;
        LoadToggles();
        UpdateActiveGuards();
    }

    private void UpdateActiveGuards() =>
        ActiveGuards = Toggles.Count(t => t.Enabled);

    // Canonical user-state path (what GuardEnforcer persists to).
    private static readonly string ProgramDataConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Argus", "Config", "GuardConfig.json");

    // Bundled defaults shipped alongside the GUI exe.
    private static readonly string BundledConfigPath = Path.Combine(
        AppContext.BaseDirectory, "Guard", "GuardConfig.json");

    private void LoadToggles()
    {
        // Prefer user state in ProgramData; fall back to bundled defaults.
        // If the runtime file has fewer categories than the bundled defaults
        // (common when Defender hasn't written a full snapshot yet), merge so
        // the UI always presents the full set with user-saved enabled flags.
        GuardConfig? user = TryLoad(ProgramDataConfigPath);
        GuardConfig? defaults = TryLoad(BundledConfigPath);

        var merged = MergeConfigs(user, defaults);
        if (merged is null)
        {
            StatusMessage = "No guard config found — reinstall to restore defaults.";
            return;
        }

        foreach (var t in merged.Toggles) Toggles.Add(t);
    }

    private static GuardConfig? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<GuardConfig>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to read guard config at {Path}", path);
            return null;
        }
    }

    private static GuardConfig? MergeConfigs(GuardConfig? user, GuardConfig? defaults)
    {
        if (user is null && defaults is null) return null;
        if (user is null) return defaults;
        if (defaults is null) return user;

        // Start from bundled defaults (full set), overlay user-saved Enabled flags by Id.
        var userEnabledById = user.Toggles
            .Where(t => !string.IsNullOrEmpty(t.Id))
            .ToDictionary(t => t.Id, t => t.Enabled);

        foreach (var t in defaults.Toggles)
            if (userEnabledById.TryGetValue(t.Id, out var enabled))
                t.Enabled = enabled;

        return defaults;
    }

    [RelayCommand]
    private void EnableAll()
    {
        foreach (var t in Toggles) t.Enabled = true;
        UpdateActiveGuards();
        StatusMessage = $"All {Toggles.Count} guards enabled — click Apply to activate.";
    }

    [RelayCommand]
    private void DisableAll()
    {
        foreach (var t in Toggles) t.Enabled = false;
        UpdateActiveGuards();
        StatusMessage = $"All {Toggles.Count} guards disabled — click Apply to persist.";
    }

    [RelayCommand]
    private void EnableCategory(string category)
    {
        var affected = Toggles.Where(t => t.Category == category).ToList();
        foreach (var t in affected) t.Enabled = true;
        UpdateActiveGuards();
        StatusMessage = $"Enabled {affected.Count} guards in '{category}'.";
    }

    [RelayCommand]
    private void DisableCategory(string category)
    {
        var affected = Toggles.Where(t => t.Category == category).ToList();
        foreach (var t in affected) t.Enabled = false;
        UpdateActiveGuards();
        StatusMessage = $"Disabled {affected.Count} guards in '{category}'.";
    }

    [RelayCommand]
    private async Task ApplySelectedAsync()
    {
        StatusMessage = "Applying privacy settings...";
        var selected = new GuardConfig
        {
            Toggles = Toggles.Where(t => t.Enabled).ToList()
        };
        await Task.Run(() => _enforcer.ApplyAll(selected));
        UpdateActiveGuards();
        StatusMessage = $"Applied {ActiveGuards} privacy settings.";
    }
}
