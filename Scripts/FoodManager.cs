using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Spawns food pellets on mouse click (raycast to water surface).
/// Food sinks slowly. Provides NearestFood API for FishController.
/// </summary>
public partial class FoodManager : Node3D
{
    [Export] public float SinkSpeed    = 0.25f;   // m/s downward
    [Export] public float FoodLifetime = 30f;     // despawn if uneaten (seconds)
    [Export] public float WaterSurfaceY = 1.5f;   // Y coordinate of water surface
    [Export] public Camera3D? GameCamera;

    private readonly List<FoodPellet> _pellets = new();

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Click input — spawn food
        if (Input.IsActionJustPressed("click"))
            TrySpawnFood();

        // Sink and age all pellets
        for (int i = _pellets.Count - 1; i >= 0; i--)
        {
            var p = _pellets[i];
            p.Position = new Vector3(p.Position.X, p.Position.Y - SinkSpeed * dt, p.Position.Z);
            p.Lifetime -= dt;
            if (p.Lifetime <= 0f || p.Position.Y < -5f)
            {
                p.Node?.QueueFree();
                _pellets.RemoveAt(i);
            }
        }
    }

    public bool HasFood() => _pellets.Count > 0;

    public float NearestDistance(Vector3 from)
    {
        float best = float.MaxValue;
        foreach (var p in _pellets)
        {
            float d = from.DistanceTo(p.Position);
            if (d < best) best = d;
        }
        return best;
    }

    public Vector3 NearestPosition(Vector3 from)
    {
        float   best    = float.MaxValue;
        Vector3 nearest = Vector3.Zero;
        foreach (var p in _pellets)
        {
            float d = from.DistanceTo(p.Position);
            if (d < best) { best = d; nearest = p.Position; }
        }
        return nearest;
    }

    public void ConsumeNearest(Vector3 from)
    {
        int   idx  = -1;
        float best = float.MaxValue;
        for (int i = 0; i < _pellets.Count; i++)
        {
            float d = from.DistanceTo(_pellets[i].Position);
            if (d < best) { best = d; idx = i; }
        }
        if (idx < 0) return;
        _pellets[idx].Node?.QueueFree();
        _pellets.RemoveAt(idx);
    }

    // ── Spawn ─────────────────────────────────────────────────────────────────

    private void TrySpawnFood()
    {
        if (GameCamera == null) return;

        Vector2 mousePos = GetViewport().GetMousePosition();
        Vector3 rayOrigin = GameCamera.ProjectRayOrigin(mousePos);
        Vector3 rayDir    = GameCamera.ProjectRayNormal(mousePos);

        // Intersect ray with horizontal water plane at WaterSurfaceY
        if (MathF.Abs(rayDir.Y) < 0.001f) return;
        float t = (WaterSurfaceY - rayOrigin.Y) / rayDir.Y;
        if (t < 0f) return;

        Vector3 spawnPos = rayOrigin + rayDir * t;
        SpawnPellet(spawnPos);
    }

    private void SpawnPellet(Vector3 pos)
    {
        // Visual: tiny orange sphere
        var mesh   = new SphereMesh { Radius = 0.04f, Height = 0.08f };
        var mat    = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.9f, 0.5f, 0.1f)
        };
        mesh.SurfaceSetMaterial(0, mat);

        var node = new MeshInstance3D { Mesh = mesh };
        AddChild(node);
        node.GlobalPosition = pos;

        _pellets.Add(new FoodPellet { Node = node, Position = pos, Lifetime = FoodLifetime });
    }

    // ── _Process syncs node positions ─────────────────────────────────────────
    // We update Position in _Process and need to push it to the node each frame.

    public override void _PhysicsProcess(double delta)
    {
        foreach (var p in _pellets)
            p.Node!.GlobalPosition = p.Position;
    }

    // ── Inner type ────────────────────────────────────────────────────────────
    private class FoodPellet
    {
        public MeshInstance3D? Node;
        public Vector3         Position;
        public float           Lifetime;
    }
}
