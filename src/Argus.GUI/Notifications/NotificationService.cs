using System.Windows;
using Argus.Core;
using Argus.GUI.IPC;
using Microsoft.Toolkit.Uwp.Notifications;
using Serilog;

namespace Argus.GUI.Notifications;

/// <summary>
/// Sends native Windows toast notifications for threat alerts
/// and protection status changes via the Windows Action Center.
/// </summary>
public sealed class NotificationService : IDisposable
{
    private readonly GuiPipeBridge _pipeBridge;
    private bool _wasConnected;
    private bool _wasSafeMode;

    public NotificationService(GuiPipeBridge pipeBridge)
    {
        _pipeBridge = pipeBridge;
        _pipeBridge.StatusUpdated += OnStatusUpdated;
        _pipeBridge.SafeModeTriggered += OnSafeModeTriggered;
    }

    private void OnStatusUpdated(ServiceStatus status)
    {
        // Detect connection state changes
        if (status.ServiceRunning && !_wasConnected)
        {
            _wasConnected = true;
            // Don't notify on initial connect — only on reconnect
        }
        else if (!status.ServiceRunning && _wasConnected)
        {
            _wasConnected = false;
            ShowNotification(
                "Protection Service Stopped",
                "The Argus Watchdog service is no longer running. Your system may not be fully protected.",
                NotificationType.Warning);
        }

        // Symmetric edge-tracking for safe mode; the actual "activated" toast
        // is fired by OnSafeModeTriggered, which uses _wasSafeMode to dedupe.
        if (!status.SafeModeActive && _wasSafeMode)
        {
            _wasSafeMode = false;
            ShowNotification(
                "Safe Mode Cleared",
                "System integrity has been restored. Protection is active.",
                NotificationType.Success);
        }
    }

    private void OnSafeModeTriggered(string reason)
    {
        // GuiPipeBridge raises this on every poll while SafeModeActive is true.
        // Only toast on the false → true edge; the "cleared" side is handled
        // in OnStatusUpdated and resets _wasSafeMode.
        if (_wasSafeMode) return;
        _wasSafeMode = true;

        ShowNotification(
            "Threat Detected — Safe Mode Activated",
            $"Argus has detected a critical threat and locked down the system. Reason: {reason}",
            NotificationType.Critical);
    }

    public void ShowThreatNotification(string fileName, string action)
    {
        ShowNotification(
            "Threat Blocked",
            $"Argus EDR {action}: {fileName}",
            NotificationType.Critical);
    }

    public void ShowScanCompleteNotification(int threatsFound)
    {
        var message = threatsFound == 0
            ? "Scan complete — no threats found."
            : $"Scan complete — {threatsFound} threat{(threatsFound == 1 ? "" : "s")} found and quarantined.";

        ShowNotification(
            "Scan Complete",
            message,
            threatsFound > 0 ? NotificationType.Warning : NotificationType.Success);
    }

    private void ShowNotification(string title, string message, NotificationType type)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .AddAttributionText("Argus EDR");

            // Add action button for critical notifications
            if (type == NotificationType.Critical)
            {
                builder.AddButton(
                    new ToastButton()
                        .SetContent("Open Argus")
                        .AddArgument("action", "open")
                        .SetBackgroundActivation());
            }

            builder.Show();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to show toast notification: {Title}", title);
        }
    }

    public void Dispose()
    {
        _pipeBridge.StatusUpdated -= OnStatusUpdated;
        _pipeBridge.SafeModeTriggered -= OnSafeModeTriggered;

        try
        {
            ToastNotificationManagerCompat.Uninstall();
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    private enum NotificationType
    {
        Success,
        Warning,
        Critical
    }
}
