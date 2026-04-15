# DeltaProject

GPU-driven fish simulation in **Godot 4.6** (C# / .NET 9).

Art direction: vivid, high-saturation palette inspired by League of Legends / Arcane — 4-band cel shading, hard specular, bright rim light, dark ink outline.

---

## Features

| Feature | Details |
|---|---|
| **Boid simulation** | Separation · Alignment · Cohesion in a GLSL compute shader |
| **GPU instancing** | `MultiMeshInstance3D` — zero CPU draw calls |
| **CPU fallback** | Automatic — activates when `RenderingDevice` is unavailable (GL Compatibility) |
| **Toon shader** | 4 cel bands · hard specular · Fresnel rim · emissive edge glow |
| **Inverted-hull outline** | Chained as `Material.NextPass` — no extra draw call overhead |
| **Per-instance variety** | Each fish gets a random colour palette tint + wave phase offset |
| **Body wave animation** | Travelling sine wave with separate head / body / tail flex envelopes |
| **Live UI** | Full parameter panel — boid rules, wave shape, shader colours — toggle with Esc |
| **Cross-platform** | PC (Forward+) · Android Vulkan · iOS Metal · falls back to GL Compatibility |

---

## Engine

- **Godot 4.6-stable mono** — `C:/Program Files/Godot/Godot_v4.6-stable_mono_win64.exe`
- **.NET 9 / Godot.NET.Sdk 4.6.0**
- Renderer: **Forward+** (desktop) · **Mobile** (Android / iOS)

---

## File Structure

```
DeltaProject/
├── project.godot
├── DeltaProject.csproj
├── Scenes/
│   └── Main.tscn               scene — two lights, camera, three script nodes
├── Scripts/
│   ├── FishManager.cs          GPU compute dispatch + CPU fallback boid loop
│   ├── FishRenderer.cs         MultiMeshInstance3D — parses bytes → transforms
│   └── FishUI.cs               runtime parameter panel (CanvasLayer)
└── Shaders/
    ├── fish_simulation.glsl    GLSL compute — separation / alignment / cohesion
    ├── fish_toon.gdshader      4-band cel + specular + rim + edge glow
    └── fish_outline.gdshader   inverted-hull outline (chained as NextPass)
```

---

## Quick Start

1. Open Godot 4.6 mono and import this folder as a project.
2. Let the editor import assets (first launch only).
3. Build C# solution: **Build → Build Solution** (or `Ctrl+B`).
4. Open `Scenes/Main.tscn`.
5. In the **Inspector** for `FishRenderer`, assign:
   - **Fish Mesh** — any low-poly fish mesh (or leave blank for capsule placeholder)
   - **Toon Material** — create a `ShaderMaterial` using `Shaders/fish_toon.gdshader`
   - **Outline Material** — create a `ShaderMaterial` using `Shaders/fish_outline.gdshader`
6. Press **F5** to run.

> The UI panel appears on the left. Press **Esc** to toggle it.

---

## Performance Targets

| Platform | Fish Count | Notes |
|---|---|---|
| Desktop (GPU) | 2048 – 4096 | O(n²) compute; stays fast on modern GPUs |
| Android (Vulkan) | 512 – 1024 | Lower default; adjust in Inspector |
| iOS / GL Compat | 256 – 512 | CPU fallback; consider a spatial hash for > 512 |

The boid loop is O(n²) — a spatial grid partition is the next step for > 4096 fish.

---

## Mobile Export Notes

- Android: requires **Vulkan** or **OpenGL ES 3.1** with compute support.
  Export preset → Renderer: `Forward+` (Vulkan) or `Mobile` (Vulkan Mobile).
- iOS: requires Metal compute (A9+ chip — iPhone 6s and later).
- GL Compatibility export automatically triggers the CPU boid fallback in `FishManager.cs`.

---

## Shader Art Direction Cheat-Sheet

All parameters are live-tweakable in the UI panel:

| Goal | Controls |
|---|---|
| Wider shadow areas | Lower `band2` and `band3` |
| Crisper cel edges | Reduce `Band Softness` |
| More vivid colours | Raise `Saturation` (> 1.5 = Arcane territory) |
| Stronger ink outline | Increase `Outline Width`; darken `Outline Color` |
| Glowing fish edge | Raise `Edge Glow` + pick a bright `Edge Glow Color` |
| Slower, lazy school | Lower `Wave Speed` + reduce `Wave Amplitude` |
| Tight darting school | Raise `Cohesion R`, raise `Alignment Weight` |
| Swirling chaotic mass | Lower `Alignment R`, raise `Separation Weight` |
