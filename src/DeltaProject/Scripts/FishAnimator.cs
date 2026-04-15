using System;
using DeltaProject.Domain;
using DeltaProject.Domain.Interfaces;
using DeltaProject.Infrastructure.Animation;
using Godot;

public partial class FishAnimator : Node
{
    [Export] public MeshInstance3D? BodyMesh;
    [Export] public MeshInstance3D? FinMesh;
    [Export] public NodePath FishControllerPath = new NodePath("../FishController");

    private IAnimSimulation _sim = null!;
    private AnimState       _state;
    private FishController? _ctrl;

    public override void _Ready()
    {
        _ctrl = GetNode<FishController>(FishControllerPath);
        try
        {
            _sim = new GpuAnimSimulation();
        }
        catch (Exception ex)
        {
            GD.Print($"FishAnimator: GPU unavailable ({ex.Message}) — using CPU fallback.");
            _sim = new CpuAnimSimulation();
        }
    }

    public override void _Process(double delta)
    {
        if (_ctrl == null) return;
        var inp = BuildInput((float)delta);
        _state  = _sim.Step(in inp);
        PushUniforms();
    }

    public override void _ExitTree() => _sim.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private AnimInput BuildInput(float dt) => new()
    {
        Speed        = _ctrl!.NormalisedSpeed,
        TurnRate     = _ctrl.TurnRate,
        WaterCurrent = _ctrl.WaterCurrentStrength,
        Elapsed      = (float)Time.GetTicksMsec() * 0.001f,
        DeltaTime    = dt,
        State        = _ctrl.State,
    };

    private void PushUniforms()
    {
        if (BodyMesh?.GetSurfaceOverrideMaterial(0) is ShaderMaterial bm)
        {
            bm.SetShaderParameter("body_phase", _state.BodyPhase);
            bm.SetShaderParameter("body_amp",   _state.BodyAmp);
            bm.SetShaderParameter("body_freq",  _state.BodyFreq);
            bm.SetShaderParameter("tail_amp",   _state.TailAmp);
            bm.SetShaderParameter("bank_angle", _state.BankAngle);
            bm.SetShaderParameter("bob_offset", MathF.Sin(_state.BobPhase) * 0.04f);
        }

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
