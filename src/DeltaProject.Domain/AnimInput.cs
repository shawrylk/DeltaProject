using System.Runtime.InteropServices;

namespace DeltaProject.Domain;

// std140 UBO layout — must match fish_swim.glsl AnimInput block exactly.
[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct AnimInput
{
    [FieldOffset( 0)] public float     Speed;         // normalised 0–1
    [FieldOffset( 4)] public float     TurnRate;      // signed rad/s
    [FieldOffset( 8)] public float     WaterCurrent;
    [FieldOffset(12)] public float     Elapsed;       // total time (s)
    [FieldOffset(16)] public float     DeltaTime;
    [FieldOffset(20)] public FishState State;
    [FieldOffset(24)] public float     Pad0;
    [FieldOffset(28)] public float     Pad1;
}
