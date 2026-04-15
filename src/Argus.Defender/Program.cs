using Argus.Core;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.File(
        Path.Combine(ArgusConstants.LogsDir, "defender-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: ArgusConstants.LogRetainedFileCountWatchdog,
        outputTemplate: ArgusConstants.LogOutputTemplate)
    .WriteTo.Console()
    .CreateLogger();

try
{
    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices(services =>
        {
            // TODO(task 3): services.AddSingleton<MonitorRegistry>();
            // TODO(task 6): services.AddSingleton<DefenderPipeServer>();
            // TODO(task 11): services.AddHostedService<HeartbeatEmitter>();
        })
        .Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Defender terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
