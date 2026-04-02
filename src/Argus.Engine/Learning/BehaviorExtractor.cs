using System.Diagnostics;

namespace Argus.Engine.Learning;

public sealed class BehaviorExtractor
{
    public BehaviorProfile ExtractFromPid(int pid)
    {
        using var proc = Process.GetProcessById(pid);

        var modules = new List<string>();
        try
        {
            foreach (ProcessModule m in proc.Modules)
                modules.Add(m.FileName ?? string.Empty);
        }
        catch (Exception)
        {
            // Access denied for protected processes — expected, not an error
        }

        int parentPid = 0;
        try
        {
            parentPid = GetParentProcessId(pid);
        }
        catch (Exception)
        {
            // May fail for system processes
        }

        return new BehaviorProfile
        {
            ProcessId = pid,
            ProcessName = proc.ProcessName,
            ImagePath = proc.MainModule?.FileName ?? string.Empty,
            ParentProcessId = parentPid,
            LoadedModules = modules,
            CapturedAt = DateTimeOffset.UtcNow
        };
    }

    private static int GetParentProcessId(int pid)
    {
        // Use WMI to get parent process ID — works even for elevated processes
        using var searcher = new System.Management.ManagementObjectSearcher(
            $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {pid}");
        foreach (var obj in searcher.Get())
            return Convert.ToInt32(obj["ParentProcessId"]);
        return 0;
    }
}
