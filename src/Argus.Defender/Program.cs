using Argus.Core;
using Argus.Defender.Dns;
using Argus.Defender.Guard;
using Argus.Defender.Hosting;
using Argus.Defender.IPC;
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
            // ── Monitor dependencies ────────────────────────────────────
            services.AddSingleton<IEnumerable<string>>(new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            });
            services.AddSingleton<IWindowsPrivacyApi, WindowsPrivacyApi>();
            services.AddSingleton(ArgusConstants.GuardConfigPath);
            services.AddSingleton<IDnsNativeApi, WindowsDnsNativeApi>();

            // ── Monitor hosts (singleton + IDefenderMonitor + IHostedService) ──
            RegisterMonitorHost<FileSystemMonitorHost>(services);
            RegisterMonitorHost<EtwMonitorHost>(services);
            RegisterMonitorHost<GuardMonitorHost>(services);
            RegisterMonitorHost<DnsMonitorHost>(services);
            RegisterMonitorHost<AmsiMonitorHost>(services);
            RegisterMonitorHost<ByovdMonitorHost>(services);
            RegisterMonitorHost<QuarantineMaintenanceHost>(services);

            // ── Registry ────────────────────────────────────────────────
            services.AddSingleton<MonitorRegistry>();

            services.AddHostedService<DefenderPipeServer>();
            services.AddHostedService<HeartbeatEmitter>();
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

static void RegisterMonitorHost<T>(IServiceCollection services)
    where T : class, IDefenderMonitor, IHostedService
{
    services.AddSingleton<T>();
    services.AddSingleton<IDefenderMonitor>(sp => sp.GetRequiredService<T>());
    services.AddHostedService(sp => sp.GetRequiredService<T>());
}
