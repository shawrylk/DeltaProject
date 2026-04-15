using System;
using System.Runtime.InteropServices;
using DeltaProject.Domain;
using DeltaProject.Domain.Interfaces;
using Godot;

namespace DeltaProject.Infrastructure.Animation;

public sealed class GpuAnimSimulation : IAnimSimulation
{
    private RenderingDevice? _rd;
    private Rid _shader;
    private Rid _inputBuf;
    private Rid _stateBuf;
    private Rid _uniformSet;
    private Rid _pipeline;

    private static readonly int InputSize = Marshal.SizeOf<AnimInput>();
    private static readonly int StateSize = Marshal.SizeOf<AnimState>();

    // Throws InvalidOperationException if GPU path is unavailable.
    public GpuAnimSimulation()
    {
        _rd = RenderingServer.CreateLocalRenderingDevice();
        if (_rd == null)
            throw new InvalidOperationException("RenderingDevice unavailable.");

        var shaderFile = GD.Load<RDShaderFile>("res://Shaders/fish_swim.glsl");
        if (shaderFile == null)
            throw new InvalidOperationException("Could not load res://Shaders/fish_swim.glsl");

        var spirv = shaderFile.GetSpirV();
        _shader   = _rd.ShaderCreateFromSpirV(spirv);

        _inputBuf = _rd.UniformBufferCreate((uint)InputSize,  new byte[InputSize]);
        _stateBuf = _rd.StorageBufferCreate((uint)StateSize,  new byte[StateSize]);

        var uniforms = new Godot.Collections.Array<RDUniform>();

        var uInput = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.UniformBuffer,
            Binding     = 0
        };
        uInput.AddId(_inputBuf);
        uniforms.Add(uInput);

        var uState = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding     = 1
        };
        uState.AddId(_stateBuf);
        uniforms.Add(uState);

        _uniformSet = _rd.UniformSetCreate(uniforms, _shader, 0);
        _pipeline   = _rd.ComputePipelineCreate(_shader);
    }

    public AnimState Step(in AnimInput input)
    {
        byte[] inpBytes = new byte[InputSize];
        var    copy     = input; // local copy for MemoryMarshal
        MemoryMarshal.Write(inpBytes, in copy);
        _rd!.BufferUpdate(_inputBuf, 0, (uint)InputSize, inpBytes);

        long list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _pipeline);
        _rd.ComputeListBindUniformSet(list, _uniformSet, 0);
        _rd.ComputeListDispatch(list, 1, 1, 1);
        _rd.ComputeListEnd();
        _rd.Submit();
        _rd.Sync();

        byte[] stateBytes = _rd.BufferGetData(_stateBuf);
        return MemoryMarshal.Read<AnimState>(stateBytes);
    }

    public void Dispose()
    {
        _rd?.FreeRid(_uniformSet);
        _rd?.FreeRid(_pipeline);
        _rd?.FreeRid(_inputBuf);
        _rd?.FreeRid(_stateBuf);
        _rd?.FreeRid(_shader);
        _rd?.Free();
    }
}
