using System;
using DeltaProject.Domain;
using DeltaProject.Domain.Interfaces;
using DeltaProject.Infrastructure.Simulation;
using Godot;

public partial class FishManager : Node
{
    [ExportGroup("Simulation")]
    [Export] public int     FishCount       = 1024;
    [Export] public float   MaxSpeed        = 6f;
    [Export] public float   MinSpeed        = 2f;
    [Export] public Vector3 BoundsSize      = new(70f, 35f, 70f);
    [Export] public float   BoundaryMargin  = 6f;

    [ExportGroup("Boid Rules")]
    [Export] public float SeparationRadius = 2.5f;
    [Export] public float AlignmentRadius  = 7f;
    [Export] public float CohesionRadius   = 9f;
    [Export] public float SeparationWeight = 1.6f;
    [Export] public float AlignmentWeight  = 1.0f;
    [Export] public float CohesionWeight   = 1.0f;

    public event Action<byte[]>? OnFishDataReady;

    private IBoidSimulation _sim = null!;

    public override void _Ready()
    {
        var config = BuildConfig();
        try
        {
            _sim = new GpuBoidSimulation();
            _sim.Initialize(config);
        }
        catch (Exception ex)
        {
            GD.Print($"FishManager: GPU unavailable ({ex.Message}) — using CPU fallback.");
            _sim = new CpuBoidSimulation();
            _sim.Initialize(config);
        }
        _sim.OnFishDataReady += bytes => OnFishDataReady?.Invoke(bytes);
    }

    public override void _PhysicsProcess(double delta) =>
        _sim.Step((float)delta, BuildConfig());

    public void Reinitialize() => _sim.Reinitialize(BuildConfig());

    public override void _ExitTree() => _sim.Dispose();

    private BoidConfig BuildConfig() => new()
    {
        FishCount        = FishCount,
        MaxSpeed         = MaxSpeed,
        MinSpeed         = MinSpeed,
        BoundsSize       = new System.Numerics.Vector3(BoundsSize.X, BoundsSize.Y, BoundsSize.Z),
        BoundaryMargin   = BoundaryMargin,
        SeparationRadius = SeparationRadius,
        AlignmentRadius  = AlignmentRadius,
        CohesionRadius   = CohesionRadius,
        SeparationWeight = SeparationWeight,
        AlignmentWeight  = AlignmentWeight,
        CohesionWeight   = CohesionWeight,
    };
}
