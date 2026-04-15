using System;
using System.Runtime.InteropServices;
using DeltaProject.Domain;
using DeltaProject.Domain.Interfaces;
using Godot;

namespace DeltaProject.Infrastructure.Simulation;

public sealed class CpuBoidSimulation : IBoidSimulation
{
    public event Action<byte[]>? OnFishDataReady;

    private FishData[]? _fish;
    private readonly Random _rng = new();

    private static readonly int FishStride = Marshal.SizeOf<FishData>();

    public void Initialize(BoidConfig config)
    {
        _fish    = new FishData[config.FishCount];
        var half = ToGodot(config.BoundsSize) * 0.5f;

        for (int i = 0; i < config.FishCount; i++)
        {
            _fish[i].Position = RandomInBox(half);
            _fish[i].Velocity = RandomDirection() * ((config.MinSpeed + config.MaxSpeed) * 0.5f);
        }
    }

    public void Step(float dt, BoidConfig config)
    {
        if (_fish == null) return;

        int     n    = _fish.Length;
        Vector3 half = ToGodot(config.BoundsSize) * 0.5f;

        float sr2 = config.SeparationRadius * config.SeparationRadius;
        float ar2 = config.AlignmentRadius  * config.AlignmentRadius;
        float cr2 = config.CohesionRadius   * config.CohesionRadius;

        for (int i = 0; i < n; i++)
        {
            Vector3 pos = _fish[i].Position;
            Vector3 vel = _fish[i].Velocity;

            Vector3 sep = Vector3.Zero, align = Vector3.Zero, coh = Vector3.Zero;
            int sc = 0, ac = 0, cc = 0;

            for (int j = 0; j < n; j++)
            {
                if (j == i) continue;
                Vector3 diff  = pos - _fish[j].Position;
                float   dist2 = diff.LengthSquared();
                if (dist2 < sr2 && dist2 > 0.0001f) { sep   += diff.Normalized() / Mathf.Sqrt(dist2); sc++; }
                if (dist2 < ar2)                     { align += _fish[j].Velocity; ac++; }
                if (dist2 < cr2)                     { coh   += _fish[j].Position; cc++; }
            }

            Vector3 accel = Vector3.Zero;
            if (sc > 0) accel += sep   / sc * config.SeparationWeight;
            if (ac > 0) accel += ((align / ac).Normalized() * config.MaxSpeed - vel) * config.AlignmentWeight * 0.15f;
            if (cc > 0) accel += (coh / cc - pos).Normalized() * config.CohesionWeight;

            static float BF(float p, float h, float m) =>
                (h - Mathf.Abs(p)) < m ? -Mathf.Sign(p) * (1f - (h - Mathf.Abs(p)) / m) * 4f : 0f;

            accel += new Vector3(BF(pos.X, half.X, config.BoundaryMargin),
                                 BF(pos.Y, half.Y, config.BoundaryMargin),
                                 BF(pos.Z, half.Z, config.BoundaryMargin));

            vel += accel * dt;
            float spd = vel.Length();
            if (spd > config.MaxSpeed) vel = vel / spd * config.MaxSpeed;
            if (spd < config.MinSpeed) vel = vel / Mathf.Max(spd, 0.0001f) * config.MinSpeed;

            pos = pos.Clamp(-half, half);
            pos += vel * dt;

            _fish[i].Position = pos;
            _fish[i].Velocity = vel;
        }

        OnFishDataReady?.Invoke(FishArrayToBytes(_fish));
    }

    public void Reinitialize(BoidConfig config) => Initialize(config);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private static byte[] FishArrayToBytes(FishData[] fish)
    {
        byte[] bytes = new byte[fish.Length * FishStride];
        for (int i = 0; i < fish.Length; i++)
        {
            IntPtr ptr = Marshal.AllocHGlobal(FishStride);
            Marshal.StructureToPtr(fish[i], ptr, false);
            Marshal.Copy(ptr, bytes, i * FishStride, FishStride);
            Marshal.FreeHGlobal(ptr);
        }
        return bytes;
    }

    private Vector3 RandomInBox(Vector3 half) =>
        new((float)(_rng.NextDouble() * 2 - 1) * half.X,
            (float)(_rng.NextDouble() * 2 - 1) * half.Y,
            (float)(_rng.NextDouble() * 2 - 1) * half.Z);

    private Vector3 RandomDirection()
    {
        var v = new Vector3((float)(_rng.NextDouble() * 2 - 1),
                            (float)(_rng.NextDouble() * 2 - 1),
                            (float)(_rng.NextDouble() * 2 - 1));
        return v.Length() < 0.001f ? Vector3.Forward : v.Normalized();
    }

    public void Dispose() => _fish = null;
}
