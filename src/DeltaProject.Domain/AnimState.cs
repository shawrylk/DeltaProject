using System.Runtime.InteropServices;

namespace DeltaProject.Domain;

// std430 SSBO layout — 10 floats = 40 bytes.  Must match fish_swim.glsl AnimState block.
[StructLayout(LayoutKind.Sequential)]
public struct AnimState
{
    public float BodyPhase;
    public float BodyAmp;
    public float BodyFreq;
    public float TailAmp;
    public float DorsalPhase;
    public float PecL;
    public float PecR;
    public float MouthOpen;
    public float BankAngle;
    public float BobPhase;
}
