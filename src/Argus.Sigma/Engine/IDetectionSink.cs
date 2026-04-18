namespace Argus.Sigma.Engine;

public interface IDetectionSink
{
    void Emit(SigmaMatch match);
}
