namespace Argus.Core.Supervision;

public record HeartbeatFrame(DateTimeOffset Timestamp)
{
    public string Type => "heartbeat";
}
