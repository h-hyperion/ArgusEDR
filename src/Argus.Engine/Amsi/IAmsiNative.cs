namespace Argus.Engine.Amsi;

public enum AmsiResult { Clean, Detected, NotDetected, Blocked }

public interface IAmsiNative
{
    AmsiResult ScanBuffer(byte[] buffer, string contentName);
}
