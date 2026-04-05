using Argus.Core;

namespace Argus.Recovery;

public sealed class SafeModeController
{
    private bool _active;
    public bool IsActive => _active;
    public event EventHandler<string>? SafeModeActivated;

    public void Activate(string reason)
    {
        _active = true;
        WriteSentinelFile(reason);
        WriteForensicReport(reason);
        SafeModeActivated?.Invoke(this, reason);
    }

    public void Deactivate()
    {
        _active = false;
        RemoveSentinelFile();
    }

    private static void WriteSentinelFile(string reason)
    {
        Directory.CreateDirectory(ArgusConstants.StateDir);
        File.WriteAllText(ArgusConstants.SafeModeSentinelPath,
            $"Activated: {DateTimeOffset.UtcNow:O}\n" +
            $"Reason: {reason}\n" +
            $"Machine: {Environment.MachineName}\n");
    }

    private static void RemoveSentinelFile()
    {
        if (File.Exists(ArgusConstants.SafeModeSentinelPath))
            File.Delete(ArgusConstants.SafeModeSentinelPath);
    }

    private static void WriteForensicReport(string reason)
    {
        var reportPath = Path.Combine(
            ArgusConstants.LogsDir,
            $"forensic_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath,
            $"ARGUS SAFE MODE REPORT\n" +
            $"Time: {DateTimeOffset.UtcNow:O}\n" +
            $"Reason: {reason}\n" +
            $"Machine: {Environment.MachineName}\n" +
            $"User: {Environment.UserName}\n");
    }
}
