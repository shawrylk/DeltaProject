# DeltaProject — Developer Guide

> GPU-driven fish simulation built on Godot 4.6 / .NET 9 / C#.  
> Clean architecture (Domain / Infrastructure / Game), compute-shader boid simulation, procedural goldfish mesh, toon shading.

---

## Table of Contents

1. [Solution Layout](#1-solution-layout)
2. [Architecture Overview](#2-architecture-overview)
3. [Boot Sequence](#3-boot-sequence)
4. [Simulation Loop](#4-simulation-loop)
5. [Domain Types](#5-domain-types)
6. [Infrastructure Implementations](#6-infrastructure-implementations)
7. [Godot Scripts Reference](#7-godot-scripts-reference)
8. [Shaders](#8-shaders)
9. [Feature Map — Where to Find Things](#9-feature-map--where-to-find-things)
10. [How to Add a Feature](#10-how-to-add-a-feature)
11. [Bug-Fixing Cheatsheet](#11-bug-fixing-cheatsheet)
12. [Code Conventions](#12-code-conventions)

---

## 1. Solution Layout

```
src/
├── DeltaProject/               # Godot entry point; thin node scripts only
│   └── Scripts/
│       ├── FishManager.cs           ← orchestrates IBoidSimulation (GPU or CPU)
│       ├── FishAnimator.cs          ← orchestrates IAnimSimulation per fish
│       ├── FishController.cs        ← single-fish AI state machine + physics
│       ├── FishRenderer.cs          ← MultiMesh GPU-instancing bridge
│       ├── GoldfishMesh.cs          ← calls GoldfishMeshBuilder; attaches to scene
│       ├── FoodManager.cs           ← food pellet spawn / scene management
│       └── FishUI.cs                ← parameter panel (CanvasLayer)
│
├── DeltaProject.Infrastructure/ # Godot-aware simulation and rendering logic
│   ├── Simulation/
│   │   ├── BoidStructs.cs           ← FishData + SimParams GPU buffer layouts
│   │   ├── GpuBoidSimulation.cs     ← fish_simulation.glsl compute dispatch
│   │   └── CpuBoidSimulation.cs     ← O(n²) CPU fallback
│   ├── Animation/
│   │   ├── GpuAnimSimulation.cs     ← fish_swim.glsl compute dispatch
│   │   └── CpuAnimSimulation.cs     ← CPU animation math fallback
│   └── Rendering/
│       └── GoldfishMeshBuilder.cs   ← procedural goldfish mesh (static builder)
│
└── DeltaProject.Domain/        # Pure C#; zero Godot imports
    ├── BoidConfig.cs                ← boid simulation parameters record
    ├── FishState.cs                 ← AI state enum (Idle/Wander/SeekFood/Eat)
    ├── AnimInput.cs                 ← animation input struct (std140 GPU layout)
    ├── AnimState.cs                 ← animation output struct (std430 GPU layout)
    └── Interfaces/
        ├── IBoidSimulation.cs
        └── IAnimSimulation.cs

DeltaProject.sln                # Solution root; references all three projects
docs/
└── UI_GUIDE.md                 ← FishUI parameter reference
```

---

## 2. Architecture Overview

```
┌────────────────────────────────────────────────────────┐
│  Godot Engine (Node scene tree)                         │
│  FishManager  FishAnimator  FishRenderer  FishUI        │
│  FoodManager  FishController  GoldfishMesh              │
└────────┬────────────────────────────────────────────────┘
         │ delegates via interfaces
┌────────▼────────────────────────────────────────────────┐
│  DeltaProject.Infrastructure                            │
│  GpuBoidSimulation / CpuBoidSimulation  (IBoidSim)      │
│  GpuAnimSimulation / CpuAnimSimulation  (IAnimSim)      │
│  GoldfishMeshBuilder  (static, pure geometry)           │
└────────┬────────────────────────────────────────────────┘
         │ depends on
┌────────▼────────────────────────────────────────────────┐
│  DeltaProject.Domain                                    │
│  BoidConfig  FishState  AnimInput  AnimState            │
│  IBoidSimulation  IAnimSimulation                       │
└────────────────────────────────────────────────────────┘
```

**Key rules:**
- **Domain is zero-Godot.** `DeltaProject.Domain` has no `using Godot` anywhere.
- **Infrastructure owns compute.** All `RenderingDevice`, `GD.Load`, and Godot math live in Infrastructure.
- **Godot scripts are thin adapters.** They hold `[Export]` fields, build config objects, and delegate to interfaces.
- **GPU path throws, Game layer catches.** Infrastructure throws `InvalidOperationException` if the GPU is unavailable; node scripts catch and fall back to CPU.
- **Scene bindings cannot move.** All 7 `.tscn`-bound scripts must stay in `src/DeltaProject/Scripts/`.

---

## 3. Boot Sequence

```
Godot loads Main.tscn
  │
  ├─ FishManager._Ready()
  │     Build BoidConfig from [Export] fields
  │     try  → new GpuBoidSimulation(); .Initialize(config)
  │     catch → new CpuBoidSimulation(); .Initialize(config)
  │     Subscribe _sim.OnFishDataReady → forward to own OnFishDataReady
  │
  ├─ FishRenderer._Ready()
  │     Subscribe FishManager.OnFishDataReady → OnFishDataReady(byte[])
  │     Build MultiMesh with FishManager.FishCount instances
  │
  ├─ FishUI._Ready()
  │     Connect all slider/spinbox signals → FishManager [Export] setters
  │     Connect "Reinitialize" button → FishManager.Reinitialize()
  │
  └─ FishTank.tscn sub-scene (one goldfish)
        ├─ GoldfishMesh._Ready()  → GoldfishMeshBuilder.Build() → assigns ArrayMesh
        ├─ FishController._Ready() → caches FoodManager node
        └─ FishAnimator._Ready()
              try  → new GpuAnimSimulation()    (ctor throws if GPU unavailable)
              catch → new CpuAnimSimulation()
```

---

## 4. Simulation Loop

### Boid simulation (`_PhysicsProcess`)

```
FishManager._PhysicsProcess(delta)
  │
  ├─ BuildConfig()           — assembles BoidConfig from current [Export] values
  ├─ _sim.Step(dt, config)   — GPU or CPU boid step
  │     GPU: upload SimParams UBO → dispatch fish_simulation.glsl → Sync → BufferGetData
  │     CPU: O(n²) separation/alignment/cohesion loop → FishArrayToBytes
  └─ fires OnFishDataReady(byte[])
        └─ FishRenderer.OnFishDataReady(byte[])
              parse byte[] → per-instance Transform3D + color in MultiMesh
```

### Animation (`_Process`)

```
FishAnimator._Process(delta)
  │
  ├─ BuildInput(dt)          — reads FishController.NormalisedSpeed / TurnRate / State
  ├─ _sim.Step(in inp)       — GPU or CPU animation step → AnimState
  └─ PushUniforms()
        body shader: body_phase, body_amp, body_freq, tail_amp, bank_angle, bob_offset
        fin shader:  body_phase, body_amp, dorsal_phase, tail_amp, bank_angle, pec_angle
```

### Single-fish physics (`_PhysicsProcess`)

```
FishController._PhysicsProcess(delta)
  │
  ├─ UpdateState()           — Idle → Wander → SeekFood → Eat state machine
  ├─ ComputeSteer(dt)        — thrust toward wander target or food pellet
  └─ Integrate(thrust, dt)   — buoyancy spring + drag + water current + velocity clamp
        AlignToVelocity(dt)  → exposes TurnRate for FishAnimator
```

---

## 5. Domain Types

All in `src/DeltaProject.Domain/` — no Godot imports.

| Type | Kind | Purpose |
|------|------|---------|
| `BoidConfig` | `sealed record` | All boid simulation parameters; created fresh each physics tick from `[Export]` fields |
| `FishState` | `enum` | AI states: `Idle`, `Wander`, `SeekFood`, `Eat` |
| `AnimInput` | `struct` (std140, 32 B) | Per-frame animation input; wraps speed, turn rate, elapsed time, state |
| `AnimState` | `struct` (std430, 40 B) | Per-frame animation output; 10 floats — phases, amplitudes, angles |
| `IBoidSimulation` | `interface` | `Initialize`, `Step`, `Reinitialize`, `OnFishDataReady`, `Dispose` |
| `IAnimSimulation` | `interface` | `Step(in AnimInput) → AnimState`, `Dispose` |

### BoidConfig

All `[Export]` boid parameters are collected into a `BoidConfig` record in `FishManager.BuildConfig()` and passed to `IBoidSimulation.Step` every tick. Changing any export field takes effect on the next physics tick automatically — no explicit flush needed.

`BoundsSize` uses `System.Numerics.Vector3` in Domain; Infrastructure converts to `Godot.Vector3` / `Godot.Vector4` at the boundary.

---

## 6. Infrastructure Implementations

All in `src/DeltaProject.Infrastructure/` — references `GodotSharp 4.6.0` NuGet.

### GpuBoidSimulation

- Created by `FishManager` at startup; throws `InvalidOperationException` if `RenderingServer.CreateLocalRenderingDevice()` returns null.
- Manages two GPU buffers: `_fishBuffer` (SSBO, `FishData[]`) and `_paramsBuffer` (UBO, `SimParams`).
- `Step`: uploads `SimParams` → dispatches `fish_simulation.glsl` with `⌈FishCount/64⌉` groups → reads back `_fishBuffer` → fires `OnFishDataReady`.
- `Reinitialize`: frees buffers, rebuilds from new `BoidConfig`.

### CpuBoidSimulation

- Maintains `FishData[]` on the heap; same `FishData` struct layout as GPU version.
- `Step`: O(n²) boid loop (separation, alignment, cohesion) + soft boundary force → serialises to `byte[]` → fires `OnFishDataReady`.

### GpuAnimSimulation

- Constructor creates local `RenderingDevice` + loads `fish_swim.glsl`; throws if unavailable.
- `Step(in AnimInput)`: writes `AnimInput` UBO → dispatches (1,1,1) → reads back `AnimState` SSBO.

### CpuAnimSimulation

- Stateful: `_state AnimState` persists between frames (smooth phase accumulation).
- `Step(in AnimInput)`: all animation math in pure C#/MathF — exponential smoothing toward target frequencies/amplitudes.

### GoldfishMeshBuilder

- **Fully static** — no instance state.
- `Build()` → returns `ArrayMesh` with 5 surfaces (body, caudal, dorsal, pec_L, pec_R).
- Uses `SurfaceTool` + the 13-ring `Prof[13,3]` profile table.

---

## 7. Godot Scripts Reference

All scripts are in `src/DeltaProject/Scripts/` and must remain there (scene `.tscn` bindings).

| Script | Extends | Role |
|--------|---------|------|
| `FishManager` | `Node` | Boid simulation orchestrator; exposes `OnFishDataReady` event and `Reinitialize()` |
| `FishAnimator` | `Node` | Animation orchestrator; reads from `FishController`, pushes uniforms to shader materials |
| `FishController` | `Node3D` | Single-fish AI (state machine) + custom physics (buoyancy, drag, current) |
| `FishRenderer` | `Node` | Converts raw boid byte array to `MultiMesh` instance transforms |
| `GoldfishMesh` | `MeshInstance3D` | Thin wrapper — calls `GoldfishMeshBuilder.Build()`; marked `[Tool]` |
| `FoodManager` | `Node3D` | Spawns food pellets on mouse click; provides `HasFood`, `NearestDistance`, `NearestPosition`, `ConsumeNearest` |
| `FishUI` | `CanvasLayer` | Full parameter panel — sliders/spinboxes for all boid and physics params |

---

## 8. Shaders

All in `src/DeltaProject/Shaders/`.

| File | Type | Purpose |
|------|------|---------|
| `fish_simulation.glsl` | Compute | Boid step: reads/writes `FishData[]` SSBO, reads `SimParams` UBO |
| `fish_swim.glsl` | Compute | Animation step: reads `AnimInput` UBO, writes `AnimState` SSBO |
| `fish_body.gdshader` | Visual | Body surface; receives `body_phase`, `body_amp`, `body_freq`, `tail_amp`, `bank_angle`, `bob_offset` |
| `fish_fins.gdshader` | Visual | Fin surfaces (1–4); receives `body_phase`, `body_amp`, `dorsal_phase`, `tail_amp`, `bank_angle`, `pec_angle` |
| `fish_toon.gdshader` | Visual | Toon shading pass |
| `fish_outline.gdshader` | Visual | Outline/rim pass |

### Struct layouts

`fish_simulation.glsl` expects:
```glsl
// SSBO binding=0 — matches FishData (32 bytes)
struct FishData { vec3 position; float pad0; vec3 velocity; float pad1; };

// UBO binding=1 — matches SimParams (64 bytes, std140)
layout(std140) uniform SimParams { uint fishCount; float dt; float sepRadius; … vec4 boundsHalf; };
```

`fish_swim.glsl` expects:
```glsl
// UBO binding=0 — matches AnimInput (32 bytes, std140)
layout(std140) uniform AnimInput { float speed; float turnRate; float waterCurrent; float elapsed; float dt; int state; float pad0; float pad1; };

// SSBO binding=1 — matches AnimState (40 bytes, std430)
struct AnimState { float bodyPhase; float bodyAmp; … float bobPhase; };  // 10 floats
```

---

## 9. Feature Map — Where to Find Things

| Feature | Primary file(s) |
|---------|----------------|
| **Boid parameters (export)** | `FishManager.cs` `[Export]` fields |
| **Boid config record** | `DeltaProject.Domain/BoidConfig.cs` |
| **GPU boid step** | `Infrastructure/Simulation/GpuBoidSimulation.cs` + `Shaders/fish_simulation.glsl` |
| **CPU boid step** | `Infrastructure/Simulation/CpuBoidSimulation.cs` |
| **Fish instance rendering** | `FishRenderer.cs`, MultiMesh |
| **Single-fish AI state machine** | `FishController.cs` — `UpdateState()` |
| **Single-fish physics** | `FishController.cs` — `Integrate()` |
| **GPU animation** | `Infrastructure/Animation/GpuAnimSimulation.cs` + `Shaders/fish_swim.glsl` |
| **CPU animation** | `Infrastructure/Animation/CpuAnimSimulation.cs` |
| **Shader uniform push** | `FishAnimator.cs` — `PushUniforms()` |
| **Goldfish geometry** | `Infrastructure/Rendering/GoldfishMeshBuilder.cs` |
| **Food spawning** | `FoodManager.cs` — `TrySpawnFood()` |
| **Food queries (AI)** | `FoodManager.cs` — `HasFood()`, `NearestDistance()`, `NearestPosition()` |
| **AI states enum** | `DeltaProject.Domain/FishState.cs` |
| **Simulation interfaces** | `DeltaProject.Domain/Interfaces/` |
| **Parameter UI** | `FishUI.cs`, `docs/UI_GUIDE.md` |
| **GPU buffer struct layouts** | `Infrastructure/Simulation/BoidStructs.cs` |
| **Animation struct layouts** | `DeltaProject.Domain/AnimInput.cs`, `AnimState.cs` |

---

## 10. How to Add a Feature

### A. New boid parameter

1. Add the field to `BoidConfig` in `Domain/BoidConfig.cs`.
2. Add the corresponding `[Export]` property to `FishManager.cs`.
3. Map it in `FishManager.BuildConfig()`.
4. Update `SimParams` in `Simulation/BoidStructs.cs` (keep std140 layout).
5. Update `GpuBoidSimulation.BuildParams()` to include the new field.
6. Update `CpuBoidSimulation.Step()` to use it.
7. Update `fish_simulation.glsl` SimParams block to match.

### B. New animation parameter

1. Add field to `AnimInput` in `Domain/AnimInput.cs` (watch std140 offsets — total must stay 32 bytes or update `Size`).
2. Feed it in `FishAnimator.BuildInput()`.
3. Update `GpuAnimSimulation.Step()` if the GPU shader needs new data.
4. Update `CpuAnimSimulation.Step()` for the CPU path.
5. Update `fish_swim.glsl` AnimInput block.

### C. New AI state

1. Add enum value to `Domain/FishState.cs`.
2. Add `case FishState.X:` to `FishController.UpdateState()` and `ComputeSteer()`.
3. If animation differs: handle the new state in `CpuAnimSimulation.Step()` (check `inp.State == FishState.X`) and update `fish_swim.glsl`.

### D. New compute shader

1. Add `MyShader.glsl` to `src/DeltaProject/Shaders/`.
2. Create `Infrastructure/MyDomain/GpuMySimulation.cs` implementing a new interface.
3. Create `Infrastructure/MyDomain/CpuMySimulation.cs` as fallback.
4. Add the interface to `Domain/Interfaces/`.
5. Wire the try/catch factory in the relevant Godot node script.

### E. New UI control

1. Add the parameter as an `[Export]` in the relevant node script.
2. Add a slider/spinbox to `FishUI.cs` following existing patterns.
3. Connect the signal → property setter in `FishUI.ConnectSignals()`.
4. Document in `docs/UI_GUIDE.md`.

---

## 11. Bug-Fixing Cheatsheet

### Fish not moving (GPU path)

1. Check `FishManager` log — if GPU init threw, it fell back to CPU (`GD.Print` shows "CPU fallback").
2. Check `fish_simulation.glsl` compiled: look for `Shaders/fish_simulation.glsl.import` in the project.
3. Check `FishRenderer` is subscribed to `FishManager.OnFishDataReady` (must connect in `_Ready`).

### Fish not moving (CPU path)

1. Check `FishManager._cpuFish` is not null — `Initialize` must have run.
2. Check `_PhysicsProcess` is firing: add a `GD.Print` counter.
3. Check `BoidConfig.FishCount` matches the `FishData[]` array length after `Reinitialize`.

### Animation frozen

1. `FishAnimator._ctrl` is null — check `FishControllerPath` NodePath export.
2. GPU anim: check `GpuAnimSimulation` ctor didn't throw silently (log shows "CPU fallback").
3. CPU anim: `_state` fields are `float` — if `DeltaTime = 0` every frame, phases never advance.

### Wrong mesh shape

1. `GoldfishMeshBuilder.Build()` is static — any change takes effect on next `GoldfishMesh._Ready()` call.
2. Surface indices: 0=body, 1=caudal, 2=dorsal, 3=pec_L, 4=pec_R. Shader material assignments must match.
3. Profile data is in `Prof[13,3]` — ring index 0 is nose, 12 is peduncle.

### Shader uniforms not updating

1. `PushUniforms()` uses `GetSurfaceOverrideMaterial(surf)` — the material must be a `ShaderMaterial` override, not the mesh's base material.
2. `bank_angle` and `pec_angle` — check the shader parameter name matches exactly (case-sensitive in Godot 4).

### UI sliders have no effect

1. Signals must be connected in `FishUI._Ready()` and target the exported properties on `FishManager`.
2. Many params only apply on the next tick via `BuildConfig()` — no manual flush needed.
3. FishCount changes require `FishManager.Reinitialize()` — buffer size must be rebuilt.

---

## 12. Code Conventions

### File size

Files approaching 300 lines are at their single-responsibility limit. Extract before adding. See global `CLAUDE.md` rules.

### Naming

- Godot node scripts: `{Domain}.cs` (e.g. `FishManager`, `FishAnimator`)
- Infrastructure classes: `{Backend}{Domain}.cs` (e.g. `GpuBoidSimulation`, `CpuAnimSimulation`)
- Domain records/structs: concept name only (e.g. `BoidConfig`, `AnimInput`)
- Interfaces: `I{Capability}.cs` (e.g. `IBoidSimulation`)

### GPU ↔ CPU fallback pattern

```csharp
// In every Godot node that owns a simulation interface:
try
{
    _sim = new GpuXxxSimulation();  // throws if RD unavailable
    // (optional: _sim.Initialize(config) if separate init step)
}
catch (Exception ex)
{
    GD.Print($"XXX: GPU unavailable ({ex.Message}) — using CPU fallback.");
    _sim = new CpuXxxSimulation();
}
```

### Struct layouts

All structs that cross the CPU↔GPU boundary carry explicit `[StructLayout]`:

```csharp
// UBO (std140) — use LayoutKind.Explicit with explicit FieldOffset
[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct AnimInput { [FieldOffset(0)] public float Speed; … }

// SSBO (std430) — sequential floats, use LayoutKind.Sequential
[StructLayout(LayoutKind.Sequential)]
public struct AnimState { public float BodyPhase; … }
```

Never reorder fields in these structs without updating the corresponding GLSL layout.

### Domain ↔ Infrastructure boundary

`BoidConfig.BoundsSize` is `System.Numerics.Vector3`. Convert to Godot types at the Infrastructure boundary:

```csharp
// In Infrastructure — converting from Domain
var half = new Godot.Vector3(config.BoundsSize.X * 0.5f, config.BoundsSize.Y * 0.5f, config.BoundsSize.Z * 0.5f);
```

Never import `System.Numerics` in Godot node scripts — use `Godot.Vector3` there and convert when building `BoidConfig`.
