using System.Runtime.InteropServices;
using Godot;

namespace DeltaProject.Infrastructure.Simulation;

// GPU buffer layouts — must match fish_simulation.glsl exactly.

[StructLayout(LayoutKind.Sequential)]
internal struct FishData
{
    public Vector3 Position; // 12 B
    public float   Pad0;     //  4 B → 16 B boundary
    public Vector3 Velocity; // 12 B
    public float   Pad1;     //  4 B → 32 B total
}

// std140 UBO
[StructLayout(LayoutKind.Explicit, Size = 64)]
internal struct SimParams
{
    [FieldOffset( 0)] public uint    FishCount;
    [FieldOffset( 4)] public float   DeltaTime;
    [FieldOffset( 8)] public float   SeparationRadius;
    [FieldOffset(12)] public float   AlignmentRadius;
    [FieldOffset(16)] public float   CohesionRadius;
    [FieldOffset(20)] public float   SeparationWeight;
    [FieldOffset(24)] public float   AlignmentWeight;
    [FieldOffset(28)] public float   CohesionWeight;
    [FieldOffset(32)] public float   MaxSpeed;
    [FieldOffset(36)] public float   MinSpeed;
    [FieldOffset(40)] public float   BoundaryMargin;
    [FieldOffset(44)] public float   Pad1;
    [FieldOffset(48)] public Vector4 BoundsHalfExtents;
}
