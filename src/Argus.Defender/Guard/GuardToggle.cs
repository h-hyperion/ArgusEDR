using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Argus.Defender.Guard;

public sealed class GuardToggle : INotifyPropertyChanged
{
    public required string Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string WhatBreaks { get; set; } = "";

    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class GuardConfig
{
    public List<GuardToggle> Toggles { get; set; } = new();

    public static GuardConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<GuardConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new GuardConfig();
    }
}
