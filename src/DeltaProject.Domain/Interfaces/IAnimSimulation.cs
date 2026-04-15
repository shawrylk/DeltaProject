namespace DeltaProject.Domain.Interfaces;

public interface IAnimSimulation : IDisposable
{
    AnimState Step(in AnimInput input);
}
