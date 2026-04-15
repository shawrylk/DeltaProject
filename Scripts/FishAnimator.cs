using Godot;
using System;
using System.Runtime.InteropServices;

/// <summary>
/// Dispatches fish_swim.glsl each frame and pushes the resulting AnimState
/// to both mesh surfaces (body and fins) as shader uniforms.
/// Attach to the same node as GoldfishMesh.
/// </summary>
public partial class FishAnimator : Node
{
    // ── Structs (byte-layout must exactly match GLSL std140 / std430) ─────────

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct AnimInput
    {
        [FieldOffset( 0)] public float Speed;        // normalised 0–1
        [FieldOffset( 4)] public float TurnRate;     // signed rad/s
        [FieldOffset( 8)] public float WaterCurrent; // current strength
        [FieldOffset(12)] public float Elapsed;      // total time (s)
        [FieldOffset(16)] public float DeltaTime;
        [FieldOffset(20)] public int   State;        // 0=idle 1=wander 2=seek 3=eat
        [FieldOffset(24)] public float Pad0;
        [FieldOffset(28)] public float Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]   // 10×float = 40 bytes, std430
    private struct AnimState
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

    // ── Exports ───────────────────────────────────────────────────────────────

    [Export] public MeshInstance3D? BodyMesh;   // surface 0 uses fish_body.gdshader
    [Export] public MeshInstance3D? FinMesh;    // surfaces 1-4 use fish_fins.gdshader
    [Export] public NodePath FishControllerPath = new NodePath("../FishController");

    // ── State ─────────────────────────────────────────────────────────────────

    private RenderingDevice?  _rd;
    private Rid               _shader;
    private Rid               _inputBuf;
    private Rid               _stateBuf;
    private Rid               _uniformSet;
    private Rid               _pipeline;
    private bool              _gpuReady;

    private AnimState         _state;   // CPU copy — persists between frames
    private FishController?   _ctrl;

    private static readonly int InputSize = Marshal.SizeOf<AnimInput>();
    private static readonly int StateSize = Marshal.SizeOf<AnimState>();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _ctrl = GetNode<FishController>(FishControllerPath);
        InitGpu();
    }

    public override void _Process(double delta)
    {
        if (_ctrl == null) return;

        var inp = BuildInput((float)delta);

        if (_gpuReady)
            DispatchGpu(inp);
        else
            SimulateCpu(inp);

        PushUniforms();
    }

    public override void _ExitTree()
    {
        if (!_gpuReady) return;
        _rd?.FreeRid(_uniformSet);
        _rd?.FreeRid(_pipeline);
        _rd?.FreeRid(_inputBuf);
        _rd?.FreeRid(_stateBuf);
        _rd?.FreeRid(_shader);
        _rd?.Free();
    }

    // ── GPU setup ─────────────────────────────────────────────────────────────

    private void InitGpu()
    {
        _rd = RenderingServer.CreateLocalRenderingDevice();
        if (_rd == null) return;

        var shaderFile = GD.Load<RDShaderFile>("res://Shaders/fish_swim.glsl");
        var spirv      = shaderFile.GetSpirV();
        _shader        = _rd.ShaderCreateFromSpirV(spirv);

        // AnimInput is a UBO (std140); AnimState is an SSBO (std430)
        _inputBuf = _rd.UniformBufferCreate((uint)InputSize,
                        new byte[InputSize]);
        _stateBuf = _rd.StorageBufferCreate((uint)StateSize,
                        new byte[StateSize]);

        var uniforms = new Godot.Collections.Array<RDUniform>();

        var uInput = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.UniformBuffer,
            Binding      = 0
        };
        uInput.AddId(_inputBuf);
        uniforms.Add(uInput);

        var uState = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding      = 1
        };
        uState.AddId(_stateBuf);
        uniforms.Add(uState);

        _uniformSet = _rd.UniformSetCreate(uniforms, _shader, 0);
        _pipeline   = _rd.ComputePipelineCreate(_shader);
        _gpuReady   = true;
    }

    // ── Per-frame GPU dispatch ─────────────────────────────────────────────────

    private void DispatchGpu(AnimInput inp)
    {
        // Upload input
        byte[] inpBytes = new byte[InputSize];
        MemoryMarshal.Write(inpBytes, ref inp);
        _rd!.BufferUpdate(_inputBuf, 0, (uint)InputSize, inpBytes);

        // Dispatch (1,1,1) — one fish
        var list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _pipeline);
        _rd.ComputeListBindUniformSet(list, _uniformSet, 0);
        _rd.ComputeListDispatch(list, 1, 1, 1);
        _rd.ComputeListEnd();
        _rd.Submit();
        _rd.Sync();

        // Read back state
        byte[] stateBytes = _rd.BufferGetData(_stateBuf);
        _state = MemoryMarshal.Read<AnimState>(stateBytes);
    }

    // ── CPU fallback (identical physics, no GPU) ───────────────────────────────

    private void SimulateCpu(AnimInput inp)
    {
        const float TAU = 6.28318530718f;
        float spd = inp.Speed;
        float dt  = inp.DeltaTime;

        float Approach(float cur, float tgt, float rate) =>
            cur + (tgt - cur) * Math.Clamp(rate * dt, 0f, 1f);

        float tgtFreq = 0.8f + spd * 3.5f;
        float tgtAmp  = 0.02f + spd * 0.13f;

        _state.BodyFreq   = Approach(_state.BodyFreq, tgtFreq, 3f);
        _state.BodyAmp    = Approach(_state.BodyAmp,  tgtAmp,  4f);
        _state.BodyPhase += _state.BodyFreq * TAU * dt;
        _state.TailAmp    = _state.BodyAmp * 2.2f;

        float dorsFreq     = 2f + spd * 2.5f;
        _state.DorsalPhase += dorsFreq * TAU * dt;

        float pecFold  = -30f * spd;
        float pecSteer = inp.TurnRate * 20f;
        _state.PecL    = Approach(_state.PecL, pecFold + pecSteer, 5f);
        _state.PecR    = Approach(_state.PecR, pecFold - pecSteer, 5f);

        float tgtMouth = (inp.State == 3) ? 1f : 0f;
        _state.MouthOpen = Approach(_state.MouthOpen, tgtMouth, 6f);

        _state.BankAngle = Approach(_state.BankAngle, inp.TurnRate * 18f, 4f);

        float bobFreq  = 0.5f + spd * 0.3f;
        _state.BobPhase += bobFreq * TAU * dt;
    }

    // ── Build AnimInput from FishController ───────────────────────────────────

    private AnimInput BuildInput(float dt) => new AnimInput
    {
        Speed        = _ctrl!.NormalisedSpeed,
        TurnRate     = _ctrl.TurnRate,
        WaterCurrent = _ctrl.WaterCurrentStrength,
        Elapsed      = (float)Time.GetTicksMsec() * 0.001f,
        DeltaTime    = dt,
        State        = (int)_ctrl.State,
    };

    // ── Push state → shader uniforms ──────────────────────────────────────────

    private void PushUniforms()
    {
        // body shader (surface 0)
        if (BodyMesh?.GetSurfaceOverrideMaterial(0) is ShaderMaterial bm)
        {
            bm.SetShaderParameter("body_phase",  _state.BodyPhase);
            bm.SetShaderParameter("body_amp",    _state.BodyAmp);
            bm.SetShaderParameter("body_freq",   _state.BodyFreq);
            bm.SetShaderParameter("tail_amp",    _state.TailAmp);
            bm.SetShaderParameter("bank_angle",  _state.BankAngle);
            bm.SetShaderParameter("bob_offset",  MathF.Sin(_state.BobPhase) * 0.04f);
        }

        // fin shader: push shared params to all fin surfaces (1=caudal, 2=dorsal, 3=pec_L, 4=pec_R)
        // If surfaces share one ShaderMaterial instance, surface 1 suffices; if not, all get updated.
        float[] pecAngles = { 0f, 0f, _state.PecL, _state.PecR };
        for (int surf = 1; surf <= 4; surf++)
        {
            if (FinMesh?.GetSurfaceOverrideMaterial(surf) is not ShaderMaterial fm) continue;
            fm.SetShaderParameter("body_phase",   _state.BodyPhase);
            fm.SetShaderParameter("body_amp",     _state.BodyAmp);
            fm.SetShaderParameter("dorsal_phase", _state.DorsalPhase);
            fm.SetShaderParameter("tail_amp",     _state.TailAmp);
            fm.SetShaderParameter("bank_angle",   _state.BankAngle);
            fm.SetShaderParameter("pec_angle",    pecAngles[surf - 1]);
        }
    }
}
