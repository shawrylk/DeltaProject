namespace DeltaProject.Domain.Interfaces;

public interface IBoidSimulation : IDisposable
{
    event Action<byte[]>? OnFishDataReady;
    void Initialize(BoidConfig config);
    void Step(float dt, BoidConfig config);
    void Reinitialize(BoidConfig config);
}
