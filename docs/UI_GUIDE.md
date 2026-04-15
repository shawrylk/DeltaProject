# DeltaProject — UI Parameter Guide

Toggle the panel with **Esc**.  All parameters update live (no Apply button).

---

## Simulation

| Parameter | Range | What it does |
|---|---|---|
| **Fish Count** | 64 – 8192 | Number of simulated fish. Changes take effect after **Reinitialize**. Keep as a multiple of 64 for full GPU warp occupancy. |
| **Bounds X / Y / Z** | 10 – 200 m | Half-extents of the invisible swim volume. Fish are pushed back when they approach the edges. |
| **Min Speed** | 0.5 – 10 | Minimum swim speed — fish below this are accelerated forward. |
| **Max Speed** | 1 – 20 | Maximum swim speed — fish above this are clamped. |
| **Bound Margin** | 1 – 25 m | Width of the soft-boundary repulsion zone inside the volume edge. |
| **Reinitialize** | Button | Destroys and recreates GPU buffers with the current Fish Count and Bounds. Required after changing either of those values. |

---

## Boid Rules

These drive the emergent schooling behaviour.

| Parameter | Range | What it does |
|---|---|---|
| **Separation R** | 0.5 – 15 m | Radius within which fish steer *away* from each other to avoid crowding. |
| **Alignment R** | 0.5 – 25 m | Radius within which fish try to *match* each other's heading. |
| **Cohesion R** | 0.5 – 30 m | Radius within which fish steer *toward* the local centre of mass. |
| **Sep Weight** | 0 – 5 | How strongly separation overrides other forces. High = loose, scattered school. |
| **Align Weight** | 0 – 5 | How tightly fish match neighbours' direction. High = synchronised waves. |
| **Cohes Weight** | 0 – 5 | How strongly fish pull toward the school centre. High = tight ball formation. |

**Tuning tips**

- Tight, flowing school: `Align Weight` 1.2, `Cohes Weight` 1.2, `Sep Weight` 1.0
- Loose, exploratory scatter: `Sep Weight` 2.5, `Cohes Weight` 0.3
- Swirling vortex: equal weights, `Alignment R` slightly smaller than `Cohesion R`

---

## Body Regions

Controls how the swim-wave deformation is distributed along the fish body.

UV.x runs **0 = snout tip → 1 = tail tip** along the fish mesh's U axis.

```
Snout          Body              Tail
  0 ─────── head_end ─────── tail_start ─────── 1
  [  Head  ][       Body        ][     Tail     ]
```

| Parameter | Range | What it does |
|---|---|---|
| **head_end** | 0.05 – 0.45 | UV position where the head region ends. Increase to give the head more wave flex. |
| **tail_start** | 0.50 – 0.95 | UV position where the tail region begins. Decrease to give the tail more length. |

The **head flex** and **tail flex** wave amplitude controls live in the Wave Animation section.

---

## Wave Animation

The fish body deforms with a travelling sine wave that moves from snout to tail
(simulating biological propulsion: the wave propagates caudal → rostral so the
reaction force drives the fish forward).

| Parameter | Range | What it does |
|---|---|---|
| **Frequency** | 0.1 – 12 | Spatial cycles of the wave along the body length. 2–3 = realistic; >5 = eel-like wiggles. |
| **Amplitude** | 0 – 0.6 | Peak lateral displacement in local mesh units. Scale to your mesh size. |
| **Speed** | 0 – 20 | How fast the wave travels (TIME multiplier). Higher = faster swimming cadence. |
| **Head Flex** | 0 – 0.3 | How much the snout area flexes. Keep near 0 for realistic fish; raise slightly for a more elastic style. |
| **Tail Flex** | 0.5 – 2.5 | Amplitude multiplier at the tail tip. 1.0 = natural taper; >1.0 = whipping tail. |

Each fish has a randomly assigned **phase offset** (set at spawn time via `INSTANCE_CUSTOM.r`)
so the school doesn't animate in perfect lockstep.

---

## Toon Shading

Controls the cel-shaded lighting model that gives the LoL / Arcane look.

### Cel Bands

NdotL (Lambert diffuse) is divided into 4 discrete colour regions:

```
0 ──── band1 ──── band2 ──── band3 ──── 1
  Deep Shadow  Shadow   Mid   Highlight
```

| Parameter | Range | What it does |
|---|---|---|
| **band1** | 0 – 0.4 | Threshold between deep shadow and shadow. Lower = more area in darkest tone. |
| **band2** | 0 – 0.8 | Threshold between shadow and mid tone. |
| **band3** | 0 – 1.0 | Threshold between mid and highlight. Raise to limit highlight to bright-facing surfaces only. |
| **Band Softness** | 0.001 – 0.1 | Width of the cross-fade between bands. Near 0 = hard ink-print cel; 0.05+ = soft cartoon. |

### Specular

| Parameter | Range | What it does |
|---|---|---|
| **Specular Threshold** | 0.80 – 1.0 | Minimum half-vector angle for specular highlight. High = tiny hot-spot. |
| **Specular Hardness** | 0.001 – 0.05 | Transition width of the specular edge. Near 0 = mirror-sharp. |

### Rim Light (Fresnel)

The rim light fires on silhouette edges facing away from the camera, giving fish a glowing
outline that reads clearly against any background — a key Arcane / LOL technique.

| Parameter | Range | What it does |
|---|---|---|
| **Rim Power** | 1 – 8 | Falloff steepness. Higher = narrower rim band on the silhouette. |
| **Rim Strength** | 0 – 4 | Brightness multiplier. |
| **Rim Threshold** | 0 – 1 | Minimum Fresnel value before rim colour fires. Raise to tighten the glow band. |

### Other

| Parameter | Range | What it does |
|---|---|---|
| **Saturation** | 0 – 3 | Boosts colour saturation in the diffuse CEL output. 1.5 gives the Arcane hyper-vivid look. Values > 2.0 will clip — intentional for stylised art. |

---

## Colour Palette

All six colour pickers feed directly into the toon shader uniforms:

| Slot | When it shows |
|---|---|
| **Deep Shadow** | NdotL < band1 — the darkest, ink-like underside |
| **Shadow** | band1 ≤ NdotL < band2 — ambient shaded areas |
| **Mid** | band2 ≤ NdotL < band3 — the dominant body colour |
| **Highlight** | NdotL ≥ band3 — direct-lit surfaces |
| **Specular** | Hot-spot glint (usually near-white) |
| **Rim** | Fresnel edge glow colour |

Tip: keep shadow → mid → highlight as a consistent hue ramp (e.g. dark blue → vibrant blue → pale cyan) for the cleanest LoL-style read.

---

## Visual

| Parameter | Range | What it does |
|---|---|---|
| **Edge Glow** | 0 – 2 | Emissive interior glow on Fresnel-facing surfaces (softer than the rim, gives a "backlit illustration" feel). |
| **Edge Glow Color** | Picker | Colour of the emissive glow. Cyan / sky blue = Arcane energy look. |
| **Instance Tint** | 0 – 1 | How strongly each fish's assigned palette colour blends into its diffuse shading. 0 = all fish same colour; 1 = strongly tinted. |
| **Outline Width** | 0 – 0.08 | Inverted-hull outline thickness in local mesh units. Increase if outlines disappear at distance (or use a camera-distance-scaled value in the shader). |
| **Outline Color** | Picker | Usually very dark navy / black for ink-print look. |

---

## Keyboard Shortcuts

| Key | Action |
|---|---|
| **Esc** | Toggle parameter panel |

---

## Tips for LoL / Arcane Look

1. **Saturation 1.4 – 1.6** is the sweet spot — colours pop without clipping to white.
2. **band1 = 0.08, band2 = 0.32, band3 = 0.65** gives the classic 4-value illustration breakdown.
3. **Outline Width 0.018 – 0.025** — thin lines on small fish, thicker on hero fish.
4. Colour the rim with a complementary hue to the mid tone (e.g. blue mid + cyan rim).
5. Keep **Head Flex near 0** and **Tail Flex 1.0 – 1.2** for anatomically believable motion.
