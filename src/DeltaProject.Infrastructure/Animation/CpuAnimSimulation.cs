using System;
using DeltaProject.Domain;
using DeltaProject.Domain.Interfaces;

namespace DeltaProject.Infrastructure.Animation;

public sealed class CpuAnimSimulation : IAnimSimulation
{
    private AnimState _state;   // persists between frames

    public AnimState Step(in AnimInput inp)
    {
        const float TAU = 6.28318530718f;
        float spd = inp.Speed;
        float dt  = inp.DeltaTime;

        float Approach(float cur, float tgt, float rate) =>
            cur + (tgt - cur) * Math.Clamp(rate * dt, 0f, 1f);

        float tgtFreq = 0.8f + spd * 3.5f;
        float tgtAmp  = 0.02f + spd * 0.13f;

        _state.BodyFreq    = Approach(_state.BodyFreq, tgtFreq, 3f);
        _state.BodyAmp     = Approach(_state.BodyAmp,  tgtAmp,  4f);
        _state.BodyPhase  += _state.BodyFreq * TAU * dt;
        _state.TailAmp     = _state.BodyAmp * 2.2f;

        float dorsFreq      = 2f + spd * 2.5f;
        _state.DorsalPhase += dorsFreq * TAU * dt;

        float pecFold  = -30f * spd;
        float pecSteer = inp.TurnRate * 20f;
        _state.PecL    = Approach(_state.PecL, pecFold + pecSteer, 5f);
        _state.PecR    = Approach(_state.PecR, pecFold - pecSteer, 5f);

        float tgtMouth   = (inp.State == FishState.Eat) ? 1f : 0f;
        _state.MouthOpen = Approach(_state.MouthOpen, tgtMouth, 6f);

        _state.BankAngle = Approach(_state.BankAngle, inp.TurnRate * 18f, 4f);

        float bobFreq   = 0.5f + spd * 0.3f;
        _state.BobPhase += bobFreq * TAU * dt;

        return _state;
    }

    public void Dispose() { }
}
