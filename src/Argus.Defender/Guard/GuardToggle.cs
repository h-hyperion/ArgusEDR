using System.Text.Json;

namespace Argus.Defender.Guard;

public sealed class GuardToggle
{
    public required string Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string WhatBreaks { get; set; } = "";
    public bool Enabled { get; set; }
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
