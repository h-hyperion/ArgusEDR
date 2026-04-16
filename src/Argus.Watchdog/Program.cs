using System.Security.Cryptography;
using Argus.Core;
using Argus.Core.Supervision;
using Argus.Watchdog;
using Argus.Watchdog.IPC;
using Argus.Watchdog.Supervision;
using Serilog;

// Logging
Log.Logger = new LoggerConfiguration()
    .WriteTo.File(
        Path.Combine(ArgusConstants.LogsDir, "watchdog-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: ArgusConstants.LogRetainedFileCountWatchdog,
        outputTemplate: ArgusConstants.LogOutputTemplate)
    .WriteTo.Console()
    .CreateLogger();

try
{
    byte[] hmacKey;
    if (File.Exists(ArgusConstants.IpcKeyPath))
    {
        var protectedKey = File.ReadAllBytes(ArgusConstants.IpcKeyPath);
        hmacKey = ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.LocalMachine);
        Log.Information("Loaded DPAPI-protected IPC key from {Path}", ArgusConstants.IpcKeyPath);
    }
    else
    {
        Log.Warning("IPC key not found at {Path} - generating ephemeral key (development mode)",
            ArgusConstants.IpcKeyPath);
        hmacKey = RandomNumberGenerator.GetBytes(32);
    }

    IHost host = Host.CreateDefaultBuilder(args)
        .UseWindowsService(opts => opts.ServiceName = ArgusConstants.WatchdogServiceName)
        .UseSerilog()
        .ConfigureServices(services =>
        {
            services.AddSingleton(hmacKey);
            services.AddSingleton(new WatchdogPipeServer(hmacKey));
            services.AddHostedService<WatchdogService>();

            // Supervisor: manages Argus.Defender child process
            services.AddSingleton<ManifestVerifier>();
            services.AddSingleton<SafeModeController>();
            services.AddSingleton<IEnumerable<ChildProcessDescriptor>>(
                _ => new[]
                {
                    new ChildProcessDescriptor(
                        Name:    "Defender",
                        ExePath: Path.Combine(ArgusConstants.InstallDir, "Argus.Defender.exe"),
                        Args:    Array.Empty<string>()),
                });
            services.AddHostedService<SupervisorService>();
        })
        .Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Watchdog terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
