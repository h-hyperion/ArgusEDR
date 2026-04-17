// Minimal test harness that emits heartbeat JSON on stdout every 1 s.
// Exits on stdin close or after a configurable number of heartbeats.
//   Usage: StubChild [maxBeats]
using System.Text.Json;

var maxBeats = args.Length > 0 && int.TryParse(args[0], out var n) ? n : int.MaxValue;
var count    = 0;

while (count < maxBeats)
{
    var frame = new { Type = "heartbeat", Timestamp = DateTimeOffset.UtcNow };
    Console.WriteLine(JsonSerializer.Serialize(frame));
    count++;
    try   { await Task.Delay(1000); }
    catch { break; }
}
