// ─────────────────────────────────────────────────────────────────────────────
// FishManager.cs
// Orchestrates GPU-side boid simulation via Godot's RenderingDevice API.
//
// On desktop (Vulkan Forward+) and modern Android (Vulkan Mobile), fish data
// lives in a GPU storage buffer and is updated each physics tick by a GLSL
// compute shader.  On platforms where RD is unavailable (e.g. GL Compatibility)
// the same logic runs on the CPU as a plain C# loop.
//
// Subscribers receive raw fish bytes via OnFishDataReady after each tick.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Runtime.InteropServices;
using Godot;

public partial class FishManager : Node
{
    // ── Inspector-exposed boid parameters ────────────────────────────────────
    [ExportGroup("Simulation")]
    [Export] public int   FishCount       = 1024;
    [Export] public float MaxSpeed        = 6f;
    [Export] public float MinSpeed        = 2f;
    [Export] public Vector3 BoundsSize    = new(70f, 35f, 70f);
    [Export] public float BoundaryMargin  = 6f;

    [ExportGroup("Boid Rules")]
    [Export] public float SeparationRadius = 2.5f;
    [Export] public float AlignmentRadius  = 7f;
    [Export] public float CohesionRadius   = 9f;
    [Export] public float SeparationWeight = 1.6f;
    [Export] public float AlignmentWeight  = 1.0f;
    [Export] public float CohesionWeight   = 1.0f;

    // ── Fires each physics tick with the raw fish byte array ─────────────────
    public event Action<byte[]>? OnFishDataReady;

    // ─── GPU path ──────────────────────────────────────────────────────────────
    private RenderingDevice? _rd;
    private Rid _shader;
    private Rid _pipeline;
    private Rid _fishBuffer;
    private Rid _paramsBuffer;
    private Rid _uniformSet;
    private bool _useGpu;

    // ─── CPU fallback path ─────────────────────────────────────────────────────
    private FishData[]? _cpuFish;
    private readonly Random _rng = new();

    // ─── Stride constants ──────────────────────────────────────────────────────
    private static readonly int FishStride   = Marshal.SizeOf<FishData>();   // 32 B
    private static readonly int ParamsStride = Marshal.SizeOf<SimParams>();  // 64 B

    // ─────────────────────────────────────────────────────────────────────────
    // GPU data types — layout must match the GLSL structs exactly.
    // ─────────────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct FishData
    {
        public Vector3 Position; // 12 B
        public float   Pad0;     //  4 B → 16 B boundary
        public Vector3 Velocity; // 12 B
        public float   Pad1;     //  4 B → 32 B total
    }

    // std140 UBO — offsets must match fish_simulation.glsl SimParams block.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct SimParams
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
        [FieldOffset(48)] public Vector4 BoundsHalfExtents; // xyz = half-extents
    }

    // ─────────────────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        _useGpu = TryInitGpu();
        if (!_useGpu) InitCpu();
    }

    // ── GPU init ─────────────────────────────────────────────────────────────
    private bool TryInitGpu()
    {
        _rd = RenderingServer.CreateLocalRenderingDevice();
        if (_rd == null)
        {
            GD.Print("FishManager: RenderingDevice unavailable — using CPU fallback.");
            return false;
        }

        var shaderFile = GD.Load<RDShaderFile>("res://Shaders/fish_simulation.glsl");
        if (shaderFile == null)
        {
            GD.PrintErr("FishManager: Could not load fish_simulation.glsl");
            _rd = null;
            return false;
        }

        var spirV   = shaderFile.GetSpirV();
        _shader     = _rd.ShaderCreateFromSpirV(spirV);
        _pipeline   = _rd.ComputePipelineCreate(_shader);

        CreateBuffers();
        return true;
    }

    private void CreateBuffers()
    {
        if (_rd == null) return;

        byte[] initial = BuildInitialFishBytes();

        _fishBuffer   = _rd.StorageBufferCreate((uint)initial.Length, initial);
        _paramsBuffer = _rd.UniformBufferCreate((uint)ParamsStride, []);

        var fishUniform = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding     = 0
        };
        fishUniform.AddId(_fishBuffer);

        var paramsUniform = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.UniformBuffer,
            Binding     = 1
        };
        paramsUniform.AddId(_paramsBuffer);

        _uniformSet = _rd.UniformSetCreate(
            [fishUniform, paramsUniform], _shader, 0);
    }

    // ── CPU init ─────────────────────────────────────────────────────────────
    private void InitCpu()
    {
        _cpuFish = new FishData[FishCount];
        var half = BoundsSize * 0.5f;
        for (int i = 0; i < FishCount; i++)
        {
            _cpuFish[i].Position = RandomInBox(half);
            _cpuFish[i].Velocity = RandomDirection() * ((MinSpeed + MaxSpeed) * 0.5f);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public: recreate everything (e.g. after UI changes FishCount / BoundsSize)
    // ─────────────────────────────────────────────────────────────────────────
    public void Reinitialize()
    {
        if (_useGpu)
        {
            FreeBuffers();
            CreateBuffers();
        }
        else
        {
            InitCpu();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    public override void _PhysicsProcess(double delta)
    {
        if (_useGpu) DispatchGpu((float)delta);
        else          StepCpu((float)delta);
    }

    // ── GPU dispatch ─────────────────────────────────────────────────────────
    private void DispatchGpu(float dt)
    {
        if (_rd == null || !_fishBuffer.IsValid) return;

        var p = BuildParams(dt);
        _rd.BufferUpdate(_paramsBuffer, 0, (uint)ParamsStride, StructToBytes(p));

        long list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _pipeline);
        _rd.ComputeListBindUniformSet(list, _uniformSet, 0);
        _rd.ComputeListDispatch(list, (uint)Mathf.CeilToInt(FishCount / 64f), 1, 1);
        _rd.ComputeListEnd();
        _rd.Submit();
        _rd.Sync();

        OnFishDataReady?.Invoke(_rd.BufferGetData(_fishBuffer));
    }

    // ── CPU boid loop (fallback) ──────────────────────────────────────────────
    private void StepCpu(float dt)
    {
        if (_cpuFish == null) return;
        int n = _cpuFish.Length;
        var half = BoundsSize * 0.5f;

        for (int i = 0; i < n; i++)
        {
            Vector3 pos = _cpuFish[i].Position;
            Vector3 vel = _cpuFish[i].Velocity;

            Vector3 sep = Vector3.Zero, align = Vector3.Zero, coh = Vector3.Zero;
            int sc = 0, ac = 0, cc = 0;

            float sr2 = SeparationRadius * SeparationRadius;
            float ar2 = AlignmentRadius  * AlignmentRadius;
            float cr2 = CohesionRadius   * CohesionRadius;

            for (int j = 0; j < n; j++)
            {
                if (j == i) continue;
                Vector3 diff  = pos - _cpuFish[j].Position;
                float   dist2 = diff.LengthSquared();
                if (dist2 < sr2 && dist2 > 0.0001f) { sep   += diff.Normalized() / Mathf.Sqrt(dist2); sc++; }
                if (dist2 < ar2)                     { align += _cpuFish[j].Velocity; ac++; }
                if (dist2 < cr2)                     { coh   += _cpuFish[j].Position; cc++; }
            }

            Vector3 accel = Vector3.Zero;
            if (sc > 0) accel += sep / sc * SeparationWeight;
            if (ac > 0) accel += ((align / ac).Normalized() * MaxSpeed - vel) * AlignmentWeight * 0.15f;
            if (cc > 0) accel += (coh / cc - pos).Normalized() * CohesionWeight;

            // Soft boundary
            static float BF(float p, float h, float m) =>
                (h - Mathf.Abs(p)) < m ? -Mathf.Sign(p) * (1f - (h - Mathf.Abs(p)) / m) * 4f : 0f;
            accel += new Vector3(BF(pos.X, half.X, BoundaryMargin),
                                  BF(pos.Y, half.Y, BoundaryMargin),
                                  BF(pos.Z, half.Z, BoundaryMargin));

            vel += accel * dt;
            float spd = vel.Length();
            if (spd > MaxSpeed) vel = vel / spd * MaxSpeed;
            if (spd < MinSpeed) vel = vel / Mathf.Max(spd, 0.0001f) * MinSpeed;

            pos = pos.Clamp(-half, half);
            pos += vel * dt;

            _cpuFish[i].Position = pos;
            _cpuFish[i].Velocity = vel;
        }

        OnFishDataReady?.Invoke(FishArrayToBytes(_cpuFish));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────
    private SimParams BuildParams(float dt) => new()
    {
        FishCount         = (uint)FishCount,
        DeltaTime         = dt,
        SeparationRadius  = SeparationRadius,
        AlignmentRadius   = AlignmentRadius,
        CohesionRadius    = CohesionRadius,
        SeparationWeight  = SeparationWeight,
        AlignmentWeight   = AlignmentWeight,
        CohesionWeight    = CohesionWeight,
        MaxSpeed          = MaxSpeed,
        MinSpeed          = MinSpeed,
        BoundaryMargin    = BoundaryMargin,
        BoundsHalfExtents = new Vector4(BoundsSize.X * 0.5f, BoundsSize.Y * 0.5f,
                                         BoundsSize.Z * 0.5f, 0f)
    };

    private byte[] BuildInitialFishBytes()
    {
        var fish = new FishData[FishCount];
        var half = BoundsSize * 0.5f;
        for (int i = 0; i < FishCount; i++)
        {
            fish[i].Position = RandomInBox(half);
            fish[i].Velocity = RandomDirection() * ((MinSpeed + MaxSpeed) * 0.5f);
        }
        return FishArrayToBytes(fish);
    }

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

    private static byte[] StructToBytes<T>(T obj) where T : struct
    {
        int    size = Marshal.SizeOf<T>();
        byte[] arr  = new byte[size];
        IntPtr ptr  = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(obj, ptr, true);
        Marshal.Copy(ptr, arr, 0, size);
        Marshal.FreeHGlobal(ptr);
        return arr;
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

    private void FreeBuffers()
    {
        if (_rd == null) return;
        if (_uniformSet.IsValid)   { _rd.FreeRid(_uniformSet);   _uniformSet   = default; }
        if (_fishBuffer.IsValid)   { _rd.FreeRid(_fishBuffer);   _fishBuffer   = default; }
        if (_paramsBuffer.IsValid) { _rd.FreeRid(_paramsBuffer); _paramsBuffer = default; }
    }

    public override void _ExitTree()
    {
        if (_rd == null) return;
        FreeBuffers();
        if (_pipeline.IsValid) _rd.FreeRid(_pipeline);
        if (_shader.IsValid)   _rd.FreeRid(_shader);
        _rd.Free();
    }
}
