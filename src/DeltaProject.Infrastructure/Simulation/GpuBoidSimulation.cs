using System;
using System.Runtime.InteropServices;
using DeltaProject.Domain;
using DeltaProject.Domain.Interfaces;
using Godot;

namespace DeltaProject.Infrastructure.Simulation;

public sealed class GpuBoidSimulation : IBoidSimulation
{
    public event Action<byte[]>? OnFishDataReady;

    private RenderingDevice? _rd;
    private Rid _shader;
    private Rid _pipeline;
    private Rid _fishBuffer;
    private Rid _paramsBuffer;
    private Rid _uniformSet;

    private static readonly int FishStride   = Marshal.SizeOf<FishData>();
    private static readonly int ParamsStride = Marshal.SizeOf<SimParams>();

    // Throws InvalidOperationException if the GPU path is unavailable.
    public void Initialize(BoidConfig config)
    {
        _rd = RenderingServer.CreateLocalRenderingDevice();
        if (_rd == null)
            throw new InvalidOperationException("RenderingDevice unavailable.");

        var shaderFile = GD.Load<RDShaderFile>("res://Shaders/fish_simulation.glsl");
        if (shaderFile == null)
            throw new InvalidOperationException("Could not load res://Shaders/fish_simulation.glsl");

        var spirv   = shaderFile.GetSpirV();
        _shader     = _rd.ShaderCreateFromSpirV(spirv);
        _pipeline   = _rd.ComputePipelineCreate(_shader);

        CreateBuffers(config);
    }

    public void Step(float dt, BoidConfig config)
    {
        if (_rd == null || !_fishBuffer.IsValid) return;

        var p = BuildParams(dt, config);
        _rd.BufferUpdate(_paramsBuffer, 0, (uint)ParamsStride, StructToBytes(p));

        long list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _pipeline);
        _rd.ComputeListBindUniformSet(list, _uniformSet, 0);
        _rd.ComputeListDispatch(list, (uint)Mathf.CeilToInt(config.FishCount / 64f), 1, 1);
        _rd.ComputeListEnd();
        _rd.Submit();
        _rd.Sync();

        OnFishDataReady?.Invoke(_rd.BufferGetData(_fishBuffer));
    }

    public void Reinitialize(BoidConfig config)
    {
        FreeBuffers();
        CreateBuffers(config);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void CreateBuffers(BoidConfig config)
    {
        if (_rd == null) return;

        byte[] initial = BuildInitialFishBytes(config);

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

        _uniformSet = _rd.UniformSetCreate([fishUniform, paramsUniform], _shader, 0);
    }

    private static SimParams BuildParams(float dt, BoidConfig c) => new()
    {
        FishCount         = (uint)c.FishCount,
        DeltaTime         = dt,
        SeparationRadius  = c.SeparationRadius,
        AlignmentRadius   = c.AlignmentRadius,
        CohesionRadius    = c.CohesionRadius,
        SeparationWeight  = c.SeparationWeight,
        AlignmentWeight   = c.AlignmentWeight,
        CohesionWeight    = c.CohesionWeight,
        MaxSpeed          = c.MaxSpeed,
        MinSpeed          = c.MinSpeed,
        BoundaryMargin    = c.BoundaryMargin,
        BoundsHalfExtents = new Vector4(c.BoundsSize.X * 0.5f, c.BoundsSize.Y * 0.5f,
                                         c.BoundsSize.Z * 0.5f, 0f)
    };

    private static byte[] BuildInitialFishBytes(BoidConfig config)
    {
        var rng  = new Random();
        var fish = new FishData[config.FishCount];
        var half = new Vector3(config.BoundsSize.X * 0.5f,
                               config.BoundsSize.Y * 0.5f,
                               config.BoundsSize.Z * 0.5f);

        for (int i = 0; i < config.FishCount; i++)
        {
            fish[i].Position = RandomInBox(rng, half);
            fish[i].Velocity = RandomDirection(rng) * ((config.MinSpeed + config.MaxSpeed) * 0.5f);
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

    private static Vector3 RandomInBox(Random rng, Vector3 half) =>
        new((float)(rng.NextDouble() * 2 - 1) * half.X,
            (float)(rng.NextDouble() * 2 - 1) * half.Y,
            (float)(rng.NextDouble() * 2 - 1) * half.Z);

    private static Vector3 RandomDirection(Random rng)
    {
        var v = new Vector3((float)(rng.NextDouble() * 2 - 1),
                            (float)(rng.NextDouble() * 2 - 1),
                            (float)(rng.NextDouble() * 2 - 1));
        return v.Length() < 0.001f ? Vector3.Forward : v.Normalized();
    }

    private void FreeBuffers()
    {
        if (_rd == null) return;
        if (_uniformSet.IsValid)   { _rd.FreeRid(_uniformSet);   _uniformSet   = default; }
        if (_fishBuffer.IsValid)   { _rd.FreeRid(_fishBuffer);   _fishBuffer   = default; }
        if (_paramsBuffer.IsValid) { _rd.FreeRid(_paramsBuffer); _paramsBuffer = default; }
    }

    public void Dispose()
    {
        FreeBuffers();
        if (_pipeline.IsValid) _rd?.FreeRid(_pipeline);
        if (_shader.IsValid)   _rd?.FreeRid(_shader);
        _rd?.Free();
    }
}
