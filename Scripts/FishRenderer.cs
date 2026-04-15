// ─────────────────────────────────────────────────────────────────────────────
// FishRenderer.cs
// Bridges FishManager (compute output) → MultiMeshInstance3D (GPU instancing).
//
// Each physics tick FishManager fires OnFishDataReady with raw bytes.
// We parse position + velocity, build a Transform3D per fish, and write it into
// the MultiMesh.  Per-instance colour and phase are written once on startup.
//
// The outline pass is chained via ShaderMaterial.NextPass so a single
// MaterialOverride drives both the toon fill and the inverted-hull outline.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using Godot;

public partial class FishRenderer : MultiMeshInstance3D
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Export] public NodePath     FishManagerPath  = "../FishManager";
    [Export] public ShaderMaterial? ToonMaterial  = null;  // fish_toon.gdshader
    [Export] public ShaderMaterial? OutlineMaterial = null; // fish_outline.gdshader
    [Export] public Mesh?        FishMesh         = null;  // assign a low-poly fish

    // Per-instance colour palette — one entry per colour slot.
    // Fish are assigned colours round-robin for variety.
    [Export] public Color[] ColorPalette = new Color[]
    {
        new(0.20f, 0.60f, 1.00f), // blue
        new(1.00f, 0.55f, 0.10f), // orange
        new(0.30f, 0.90f, 0.50f), // green
        new(1.00f, 0.25f, 0.40f), // red
        new(0.85f, 0.25f, 1.00f), // purple
        new(1.00f, 0.90f, 0.15f), // yellow
    };

    private const int Stride = 32; // bytes per FishData (vec3 + float + vec3 + float)

    private MultiMesh  _multiMesh = null!;
    private FishManager _fm       = null!;
    private float[]     _phases   = Array.Empty<float>();
    private readonly Random _rng  = new();

    // ─────────────────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        _fm = GetNode<FishManager>(FishManagerPath);

        _multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors       = true,
            UseCustomData   = true,   // INSTANCE_CUSTOM.r = per-fish wave phase
            InstanceCount   = _fm.FishCount,
            Mesh            = FishMesh ?? MakeDefaultMesh(),
        };

        Multimesh        = _multiMesh;
        MaterialOverride = BuildMaterial();
        CastShadow       = GeometryInstance3D.ShadowCastingSetting.Off; // perf: skip shadow pass

        InitPerInstanceData(_fm.FishCount);
        _fm.OnFishDataReady += OnFishDataReady;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Build the two-pass material: toon fill + outline chain.
    // ─────────────────────────────────────────────────────────────────────────
    private ShaderMaterial BuildMaterial()
    {
        ShaderMaterial toon = ToonMaterial ?? new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://Shaders/fish_toon.gdshader")
        };

        ShaderMaterial outline = OutlineMaterial ?? new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://Shaders/fish_outline.gdshader")
        };

        toon.NextPass = outline;
        return toon;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Assign random phase [0..1] and palette colour per instance.
    // ─────────────────────────────────────────────────────────────────────────
    private void InitPerInstanceData(int count)
    {
        _phases = new float[count];
        for (int i = 0; i < count; i++)
        {
            _phases[i] = (float)_rng.NextDouble();
            _multiMesh.SetInstanceCustomData(i, new Color(_phases[i], 0f, 0f, 0f));

            Color tint = ColorPalette[i % ColorPalette.Length];
            // Slight brightness variation per fish.
            float br = 0.85f + (float)_rng.NextDouble() * 0.30f;
            _multiMesh.SetInstanceColor(i, new Color(tint.R * br, tint.G * br, tint.B * br));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Called each physics tick by FishManager.
    // ─────────────────────────────────────────────────────────────────────────
    private void OnFishDataReady(byte[] data)
    {
        int count = Math.Min(data.Length / Stride, _multiMesh.InstanceCount);
        for (int i = 0; i < count; i++)
        {
            int o = i * Stride;
            // Read position bytes 0-11
            float px = BitConverter.ToSingle(data, o);
            float py = BitConverter.ToSingle(data, o + 4);
            float pz = BitConverter.ToSingle(data, o + 8);
            // bytes 12-15 = pad0 (skip)
            // Read velocity bytes 16-27
            float vx = BitConverter.ToSingle(data, o + 16);
            float vy = BitConverter.ToSingle(data, o + 20);
            float vz = BitConverter.ToSingle(data, o + 24);

            var pos = new Vector3(px, py, pz);
            var vel = new Vector3(vx, vy, vz);

            _multiMesh.SetInstanceTransform(i, FishTransform(pos, vel));
        }
    }

    // ── Fish faces the direction of its velocity ──────────────────────────────
    private static Transform3D FishTransform(Vector3 position, Vector3 velocity)
    {
        Vector3 fwd = velocity.LengthSquared() > 0.001f ? velocity.Normalized() : Vector3.Forward;

        // Choose an 'up' that isn't parallel to forward.
        Vector3 worldUp = Mathf.Abs(fwd.Dot(Vector3.Up)) > 0.99f ? Vector3.Back : Vector3.Up;
        Vector3 right   = fwd.Cross(worldUp).Normalized();
        Vector3 up      = right.Cross(fwd).Normalized();

        // Godot: -Z is the default forward of a mesh.
        return new Transform3D(new Basis(right, up, -fwd), position);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Called by FishUI to update toon / outline shader parameters live.
    // ─────────────────────────────────────────────────────────────────────────
    public void SetShaderParameter(string param, Variant value)
    {
        if (MaterialOverride is ShaderMaterial sm)
        {
            sm.SetShaderParameter(param, value);
            // Mirror wave params to outline pass so it stays in sync.
            if (sm.NextPass is ShaderMaterial outline)
                outline.SetShaderParameter(param, value);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fallback mesh: flattened capsule approximating a fish body.
    // Replace with a proper low-poly fish in the editor.
    // ─────────────────────────────────────────────────────────────────────────
    private static CapsuleMesh MakeDefaultMesh()
    {
        return new CapsuleMesh
        {
            Radius = 0.14f,
            Height = 0.9f,
            RadialSegments = 8,
            Rings = 3,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    public override void _ExitTree()
    {
        if (_fm != null)
            _fm.OnFishDataReady -= OnFishDataReady;
    }
}
