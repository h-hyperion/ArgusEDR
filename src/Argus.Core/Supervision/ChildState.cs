namespace Argus.Core.Supervision;

public enum ChildState
{
    NotStarted,
    Starting,
    Running,
    Restarting,
    Failed,
    SafeMode
}
