// src\Argus.Defender\Etw\EtwEvent.cs
namespace Argus.Defender.Etw;

public enum EtwEventType { ProcessStart, ProcessStop, RegistryChange }

public sealed record EtwEvent
{
    public required EtwEventType Type { get; init; }
    public int ProcessId { get; init; }
    public int ParentProcessId { get; init; }
    public string? ImageName { get; init; }
    public string? CommandLine { get; init; }
    public string? RegistryKey { get; init; }
    public string? RegistryValue { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
