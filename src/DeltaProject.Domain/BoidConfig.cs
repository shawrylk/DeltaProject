using System.Numerics;

namespace DeltaProject.Domain;

public sealed record BoidConfig
{
    public int     FishCount        { get; init; } = 1024;
    public float   MaxSpeed         { get; init; } = 6f;
    public float   MinSpeed         { get; init; } = 2f;
    public Vector3 BoundsSize       { get; init; } = new(70f, 35f, 70f);
    public float   BoundaryMargin   { get; init; } = 6f;
    public float   SeparationRadius { get; init; } = 2.5f;
    public float   AlignmentRadius  { get; init; } = 7f;
    public float   CohesionRadius   { get; init; } = 9f;
    public float   SeparationWeight { get; init; } = 1.6f;
    public float   AlignmentWeight  { get; init; } = 1.0f;
    public float   CohesionWeight   { get; init; } = 1.0f;
}
