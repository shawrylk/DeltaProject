// ─────────────────────────────────────────────────────────────────────────────
// fish_toon.gdshader
// LoL / Arcane-style toon shader for fish.
//
// Art direction: vivid, saturated palette; 4-band cel lighting; hard specular;
// bright rim light (Fresnel); emissive highlight edge; per-instance colour tint.
//
// Body animation: travelling sine-wave deformation along the fish body, driven
// by TIME.  UV.x = 0 at the snout, UV.x = 1 at the tail tip.
// Per-instance phase offset comes from INSTANCE_CUSTOM.r (set by FishRenderer).
//
// Mobile: all techniques are compatible with the Godot 4 Mobile renderer
// (no compute required in this file — only used by FishManager).
// ─────────────────────────────────────────────────────────────────────────────

shader_type spatial;
render_mode cull_back, depth_draw_opaque, blend_mix;

// ── Colour palette ────────────────────────────────────────────────────────────
uniform vec4 color_deep_shadow  : source_color = vec4(0.02, 0.04, 0.18, 1.0);
uniform vec4 color_shadow       : source_color = vec4(0.04, 0.14, 0.42, 1.0);
uniform vec4 color_mid          : source_color = vec4(0.06, 0.44, 0.88, 1.0);
uniform vec4 color_highlight    : source_color = vec4(0.22, 0.78, 1.00, 1.0);
uniform vec4 color_specular     : source_color = vec4(0.95, 1.00, 1.00, 1.0);
uniform vec4 color_rim          : source_color = vec4(0.40, 0.90, 1.00, 1.0);
// Instance tint blend (0 = ignore per-instance color, 1 = full tint)
uniform float instance_tint_strength : hint_range(0.0, 1.0) = 0.35;

// ── Cel shading bands ─────────────────────────────────────────────────────────
// NdotL thresholds: raise band1/2/3 to push shadows in; lower to widen them.
uniform float band1         : hint_range(0.0, 0.5)  = 0.10; // deep shadow → shadow
uniform float band2         : hint_range(0.0, 1.0)  = 0.35; // shadow → mid
uniform float band3         : hint_range(0.0, 1.0)  = 0.68; // mid → highlight
uniform float band_softness : hint_range(0.001, 0.08) = 0.025; // transition width

// ── Specular ──────────────────────────────────────────────────────────────────
uniform float specular_threshold : hint_range(0.80, 1.0)  = 0.91;
uniform float specular_hardness  : hint_range(0.001, 0.05) = 0.02;

// ── Rim light ─────────────────────────────────────────────────────────────────
uniform float rim_power     : hint_range(1.0, 8.0)  = 3.5;
uniform float rim_strength  : hint_range(0.0, 4.0)  = 1.6;
uniform float rim_threshold : hint_range(0.0, 1.0)  = 0.45;

// ── Saturation boost ─────────────────────────────────────────────────────────
// >1.0 pushes colours toward vivid LOL palette.
uniform float saturation : hint_range(0.0, 3.0) = 1.5;

// ── Emissive edge glow ────────────────────────────────────────────────────────
// Subtle interior glow on bright-facing surfaces, like Arcane cel lines.
uniform float edge_glow_strength : hint_range(0.0, 2.0) = 0.4;
uniform vec4  edge_glow_color : source_color = vec4(0.5, 0.95, 1.0, 1.0);

// ── Body region boundaries (UV.x: 0 = snout, 1 = tail tip) ───────────────────
uniform float head_end   : hint_range(0.0, 0.5)  = 0.18;
uniform float tail_start : hint_range(0.5, 1.0)  = 0.62;

// ── Sine-wave animation ───────────────────────────────────────────────────────
varying vec3 v_instance_color;

uniform float wave_frequency : hint_range(0.1, 12.0) = 2.5;
uniform float wave_amplitude : hint_range(0.0, 0.6)  = 0.13;
uniform float wave_speed     : hint_range(0.0, 20.0) = 5.0;
uniform float head_flex      : hint_range(0.0, 0.3)  = 0.02;  // how much snout flexes
uniform float tail_flex      : hint_range(0.5, 2.5)  = 1.05;  // amplitude multiplier at tip

// ─────────────────────────────────────────────────────────────────────────────
// Vertex stage — body wave deformation
// ─────────────────────────────────────────────────────────────────────────────
void vertex() {
    float t = UV.x; // 0 = snout, 1 = tail tip

    // Build flex envelope: near-zero at snout, peaks at tail.
    float flex;
    float inv_head = 1.0 / max(head_end, 0.001);
    float inv_body = 1.0 / max(tail_start - head_end, 0.001);
    float inv_tail = 1.0 / max(1.0 - tail_start, 0.001);

    if (t < head_end) {
        flex = head_flex * (t * inv_head);
    } else if (t > tail_start) {
        float tail_t = (t - tail_start) * inv_tail;
        flex = mix(1.0, tail_flex, tail_t);
    } else {
        flex = mix(head_flex, 1.0, (t - head_end) * inv_body);
    }

    v_instance_color = COLOR.rgb;

    // Per-instance phase from INSTANCE_CUSTOM.r (0..1, mapped to 0..TAU).
    float phase = INSTANCE_CUSTOM.r * TAU;

    // Travelling wave: moves tail→snout direction, propelling fish forward.
    float wave = sin(TIME * wave_speed - t * wave_frequency * TAU + phase)
                 * wave_amplitude * flex;

    VERTEX.x += wave;
}

// ─────────────────────────────────────────────────────────────────────────────
// Fragment stage — rim glow + base albedo
// ─────────────────────────────────────────────────────────────────────────────
void fragment() {
    // Rim / Fresnel — computed in fragment so VIEW is available.
    float cosV  = max(0.0, dot(NORMAL, VIEW));
    float rim   = pow(1.0 - cosV, rim_power);
    float rim_f = smoothstep(rim_threshold - 0.02, rim_threshold + 0.02, rim);
    EMISSION = color_rim.rgb * rim_f * rim_strength;

    // Edge glow: bright Fresnel facing slightly toward camera (inside the rim band).
    float edge = pow(1.0 - cosV, rim_power * 0.5);
    float edge_f = smoothstep(0.3, 0.7, edge) * (1.0 - rim_f);
    EMISSION += edge_glow_color.rgb * edge_f * edge_glow_strength;

    // Base albedo — lighting handled in light().  Use mid tone as neutral base.
    // Instance tint from MultiMesh.SetInstanceColor() blends into the palette.
    vec3 tinted_mid = mix(color_mid.rgb, color_mid.rgb * v_instance_color * 2.0,
                          instance_tint_strength);
    ALBEDO    = tinted_mid;
    METALLIC  = 0.0;
    ROUGHNESS = 0.95; // matte — specular is handled manually in light()
    SPECULAR  = 0.0;
}

// ─────────────────────────────────────────────────────────────────────────────
// Light stage — called once per light per fragment
// ─────────────────────────────────────────────────────────────────────────────
void light() {
    // NdotL for diffuse band calculation.
    float NdotL = max(0.0, dot(NORMAL, LIGHT));

    // ── 4-band cel shading ────────────────────────────────────────────────
    vec3 cel;
    float s = band_softness;
    if (NdotL < band1) {
        cel = color_deep_shadow.rgb;
    } else if (NdotL < band2) {
        float t = smoothstep(band1 - s, band1 + s, NdotL);
        cel = mix(color_deep_shadow.rgb, color_shadow.rgb, t);
    } else if (NdotL < band3) {
        float t = smoothstep(band2 - s, band2 + s, NdotL);
        cel = mix(color_shadow.rgb, color_mid.rgb, t);
    } else {
        float t = smoothstep(band3 - s, band3 + s, NdotL);
        cel = mix(color_mid.rgb, color_highlight.rgb, t);
    }

    // Blend per-instance tint into diffuse.
    cel = mix(cel, cel * v_instance_color * 1.8, instance_tint_strength);

    // ── Saturation boost (LOL vibrancy) ───────────────────────────────────
    float luma = dot(cel, vec3(0.299, 0.587, 0.114));
    cel = clamp(mix(vec3(luma), cel, saturation), 0.0, 1.0);

    DIFFUSE_LIGHT += cel * LIGHT_COLOR * ATTENUATION;

    // ── Hard specular (Blinn-Phong threshold) ─────────────────────────────
    float spec = dot(NORMAL, normalize(LIGHT + VIEW));
    float spec_mask = smoothstep(specular_threshold - specular_hardness,
                                 specular_threshold + specular_hardness, spec);
    SPECULAR_LIGHT += color_specular.rgb * spec_mask * LIGHT_COLOR * ATTENUATION;
}
