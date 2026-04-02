// src\Argus.Defender\Etw\EtwConsumer.cs
using System.Threading.Channels;
using Argus.Defender.Monitors;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Serilog;

namespace Argus.Defender.Etw;

/// <summary>
/// Hybrid ETW consumer for process creation and registry change events.
/// Requires SYSTEM privilege to subscribe to kernel ETW providers.
/// Full ETW file monitoring deferred to v2.2.
/// </summary>
public sealed class EtwConsumer : IDisposable
{
    private readonly Channel<EtwEvent> _channel;
    private TraceEventSession? _session;
    private readonly EventPipeline? _pipeline;

    public EtwConsumer(int maxQueueSize = 1000, EventPipeline? pipeline = null)
    {
        _pipeline = pipeline;
        _channel = Channel.CreateBounded<EtwEvent>(new BoundedChannelOptions(maxQueueSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    public ChannelReader<EtwEvent> Events => _channel.Reader;

    /// <summary>
    /// Start listening to kernel ETW events. Must run as SYSTEM.
    /// </summary>
    public Task StartAsync(CancellationToken ct)
    {
        return Task.Run(() =>
        {
            try
            {
                _session = new TraceEventSession("ArgusEtwSession");
                _session.EnableKernelProvider(
                    KernelTraceEventParser.Keywords.Process |
                    KernelTraceEventParser.Keywords.Registry);

                _session.Source.Kernel.ProcessStart += OnProcessStart;
                _session.Source.Kernel.ProcessStop += OnProcessStop;
                _session.Source.Kernel.RegistrySetValue += OnRegistryChange;

                ct.Register(() => _session?.Stop());

                Log.Information("ETW consumer started — listening for process and registry events");
                _session.Source.Process(); // Blocks until session stops
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ETW consumer failed — process/registry monitoring unavailable");
            }
        }, ct);
    }

    private void OnProcessStart(ProcessTraceData data)
    {
        var evt = new EtwEvent
        {
            Type = EtwEventType.ProcessStart,
            ProcessId = data.ProcessID,
            ParentProcessId = data.ParentID,
            ImageName = data.ImageFileName,
            CommandLine = data.CommandLine
        };

        _channel.Writer.TryWrite(evt);
        _pipeline?.TryPublish(new MonitorEvent
        {
            Type = MonitorEventType.ProcessStarted,
            Path = data.ImageFileName ?? "",
            ProcessId = data.ProcessID,
            ProcessName = data.ImageFileName
        });
    }

    private void OnProcessStop(ProcessTraceData data)
    {
        var evt = new EtwEvent
        {
            Type = EtwEventType.ProcessStop,
            ProcessId = data.ProcessID,
            ImageName = data.ImageFileName
        };
        _channel.Writer.TryWrite(evt);
    }

    private void OnRegistryChange(RegistryTraceData data)
    {
        var evt = new EtwEvent
        {
            Type = EtwEventType.RegistryChange,
            RegistryKey = data.KeyName,
            RegistryValue = data.ValueName,
            ProcessId = data.ProcessID
        };

        _channel.Writer.TryWrite(evt);
        _pipeline?.TryPublish(new MonitorEvent
        {
            Type = MonitorEventType.RegistryChanged,
            Path = data.KeyName ?? "",
            RegistryKey = data.KeyName,
            ProcessId = data.ProcessID
        });
    }

    public void Dispose()
    {
        _session?.Stop();
        _session?.Dispose();
        _channel.Writer.TryComplete();
    }
}
