namespace Argus.Core;

/// <summary>
/// Centralized constants for Argus EDR. All file paths, registry keys, service names,
/// and pipe names are defined here. Never hardcode these values in individual modules.
/// </summary>
public static class ArgusConstants
{
    // ── Version ─────────────────────────────────────────────────────────────
    public const string Version = "2.1.0";

    // ── File System Paths ───────────────────────────────────────────────────
    public static readonly string DataRoot       = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Argus");
    public static readonly string BackupsDir     = Path.Combine(DataRoot, "Backups");
    public static readonly string LogsDir        = Path.Combine(DataRoot, "Logs");
    public static readonly string ConfigDir      = Path.Combine(DataRoot, "Config");
    public static readonly string QuarantineDir  = Path.Combine(DataRoot, "Quarantine");
    public static readonly string YaraDir        = Path.Combine(DataRoot, "YARA");
    public static readonly string CanariesDir    = Path.Combine(DataRoot, "Canaries");
    public static readonly string StateDir       = Path.Combine(DataRoot, "State");
    public static readonly string InstallDir     = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Argus");

    // ── Sentinel & Config Files ─────────────────────────────────────────────
    public static readonly string SafeModeSentinelPath = Path.Combine(StateDir, "argus.safemode");
    public static readonly string IpcKeyPath           = Path.Combine(ConfigDir, "ipc.key");
    public static readonly string GuardConfigPath      = Path.Combine(ConfigDir, "GuardConfig.json");
    public static readonly string ApiKeysPath          = Path.Combine(ConfigDir, "api_keys.json");
    public static readonly string YaraManifestPath     = Path.Combine(YaraDir, "manifest.sha256");

    // ── Sentinel & Config Files (continued) ────────────────────────────────
    public static readonly string MonitorConfigPath = Path.Combine(ConfigDir, "MonitorConfig.json");
    public static readonly string ManifestPath      = Path.Combine(InstallDir, "manifest.json");

    // ── Named Pipe ──────────────────────────────────────────────────────────
    public const string PipeName        = "ArgusEDR";
    public const string PipeFullPath    = @"\\.\pipe\ArgusEDR";
    public const string DefenderPipeName = "argus-defender";

    // ── Windows Service Names ───────────────────────────────────────────────
    public const string WatchdogServiceName = "ArgusWatchdog";

    // ── Registry Keys ───────────────────────────────────────────────────────
    public const string RegistryRoot     = @"HKEY_LOCAL_MACHINE\SOFTWARE\Argus";
    public const string RegistryRootKey  = @"SOFTWARE\Argus";
    public const string IntegritySubKey  = @"SOFTWARE\Argus\Integrity";

    // ── Module Identifiers (used in IPC SenderModule field) ─────────────────
    public const string ModuleWatchdog = "Watchdog";
    public const string ModuleDefender = "Defender";
    public const string ModuleScanner  = "Scanner";
    public const string ModuleRecovery = "Recovery";
    public const string ModuleEngine   = "Engine";
    public const string ModuleGui      = "GUI";

    // ── Timing Constants ────────────────────────────────────────────────────
    public static readonly TimeSpan HeartbeatInterval        = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan HeartbeatTimeout          = TimeSpan.FromSeconds(10);
    public const int HeartbeatIntervalSeconds          = 5;
    public const int HeartbeatTimeoutSeconds           = 10;
    public const int MaxConsecutiveRestartFailures     = 5;
    public static readonly TimeSpan HashVerificationInterval  = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan CanaryCheckInterval       = TimeSpan.FromSeconds(60);
    public const int EventPipelineCapacity = 50_000;

    // ── IPC Protocol ────────────────────────────────────────────────────────
    public const int IpcProtocolVersion = 1;
    public const int IpcMaxPayloadBytes = 1_048_576; // 1 MB
    public const int IpcHmacSizeBytes   = 32;        // HMAC-SHA256

    // ── SQLite ──────────────────────────────────────────────────────────────
    public const string SqlitePragmas = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";

    // ── Logging ─────────────────────────────────────────────────────────────
    public const int LogRetainedFileCountDefault = 30;
    public const int LogRetainedFileCountWatchdog = 90;
    public const string LogOutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}";
}
