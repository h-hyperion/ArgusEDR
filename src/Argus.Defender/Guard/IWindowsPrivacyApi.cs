namespace Argus.Defender.Guard;

/// <summary>
/// Abstraction over Windows registry and service APIs for testability.
/// Production implementation uses Registry + ServiceController.
/// Unit tests mock this interface.
/// </summary>
public interface IWindowsPrivacyApi
{
    void SetRegistryValue(string keyPath, string valueName, object value);
    void DeleteRegistryValue(string keyPath, string valueName);
    void StopAndDisableService(string serviceName);
    void DisableScheduledTask(string taskPath);
}
