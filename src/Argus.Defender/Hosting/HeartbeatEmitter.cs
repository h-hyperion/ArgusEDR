using System.Text.Json;
using Argus.Core;
using Argus.Core.Supervision;
using Microsoft.Extensions.Hosting;

namespace Argus.Defender.Hosting;

public sealed class HeartbeatEmitter : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var frame = new HeartbeatFrame(DateTimeOffset.UtcNow);
            Console.WriteLine(JsonSerializer.Serialize(frame));
            await Task.Delay(TimeSpan.FromSeconds(ArgusConstants.HeartbeatIntervalSeconds), ct);
        }
    }
}
