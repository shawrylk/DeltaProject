shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_toon, specular_toon;

// ── Animation uniforms (mirrored from FishAnimator) ───────────────────────────
uniform float body_phase    : hint_range(-6.28318, 6.28318) = 0.0;
uniform float body_amp      : hint_range(0.0, 0.5)          = 0.05;
uniform float dorsal_phase  : hint_range(-6.28318, 6.28318) = 0.0;
uniform float pec_angle     : hint_range(-60.0, 60.0)       = 0.0;  // per-fin, set by FishAnimator
uniform float tail_amp      : hint_range(0.0, 1.0)          = 0.11;
uniform float bank_angle    : hint_range(-45.0, 45.0)       = 0.0;

// ── Appearance ────────────────────────────────────────────────────────────────
uniform vec4  col_fin_base   : source_color = vec4(1.00, 0.65, 0.15, 0.72);
uniform vec4  col_fin_tip    : source_color = vec4(1.00, 0.85, 0.55, 0.45);
uniform vec4  col_fin_ray    : source_color = vec4(0.80, 0.40, 0.05, 0.85);
uniform float ray_density    : hint_range(2.0, 20.0) = 8.0;
uniform float ray_width      : hint_range(0.01, 0.15) = 0.04;
uniform float roughness      : hint_range(0.0, 1.0)   = 0.55;

// Cel thresholds (kept in sync with body shader via FishAnimator)
uniform float band1          : hint_range(0.0, 0.4)    = 0.08;
uniform float band2          : hint_range(0.0, 0.8)    = 0.32;
uniform float band3          : hint_range(0.0, 1.0)    = 0.65;
uniform float band_soft      : hint_range(0.001, 0.1)  = 0.015;
uniform float saturation     : hint_range(0.0, 3.0)    = 1.45;

// Surface index passed as INSTANCE_CUSTOM.b by FishAnimator:
//   1 = caudal, 2 = dorsal, 3 = pec_L, 4 = pec_R

varying vec2  v_uv;
varying vec3  v_normal_ws;
varying vec3  v_pos_ws;

vec3 sat(vec3 c, float s) {
    float lum = dot(c, vec3(0.213, 0.715, 0.072));
    return mix(vec3(lum), c, s);
}

void vertex() {
    float surf = INSTANCE_CUSTOM.b;   // surface identifier
    float u    = UV.x;                // 0=root 1=tip (body UV.x convention)

    // ── Caudal fin: continue body wave at u=1 (tail amplitude)
    if (surf < 1.5) {
        // Caudal deforms with tail_amp * sin(phase) — no spatial frequency,
        // just a rigid lateral swing matching the tail beat.
        float swing = sin(body_phase) * tail_amp;
        VERTEX.x += swing * u;   // u ramps 0→1 from peduncle to fin tip
    }

    // ── Dorsal fin: high-frequency flutter proportional to UV.y
    if (surf > 1.5 && surf < 2.5) {
        float flutter = sin(dorsal_phase) * body_amp * 0.4;
        VERTEX.x += flutter * UV.y;   // UV.y = 0 at base, scales toward tip
    }

    // ── Pectoral fins: rotate around attachment point by pec_angle
    if (surf > 2.5) {
        float rad = radians(pec_angle);
        float side = (surf < 3.5) ? -1.0 : 1.0;   // 3=left, 4=right
        float cr = cos(rad * side), sr = sin(rad * side);
        vec3 v = VERTEX;
        // Rotate in XZ plane (sweep forward/back)
        VERTEX.x = v.x * cr - v.z * sr;
        VERTEX.z = v.x * sr + v.z * cr;
    }

    // ── Bank rotation (shared with body) ─────────────────────────────────────
    float bank_rad = radians(bank_angle);
    float cx = cos(bank_rad), sx = sin(bank_rad);
    vec3 v = VERTEX;
    VERTEX.y = v.y * cx - v.x * sx;
    VERTEX.x = v.y * sx + v.x * cx;

    v_uv        = UV;
    v_pos_ws    = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    v_normal_ws = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

// ── Fin ray pattern ───────────────────────────────────────────────────────────
float ray_mask(vec2 uv) {
    float ray = fract(uv.x * ray_density);
    return smoothstep(ray_width, ray_width + 0.02, abs(ray - 0.5));
}

void fragment() {
    float u = v_uv.x;   // 0=base(root) 1=tip

    // Base-to-tip opacity/colour gradient
    vec4 col  = mix(col_fin_base, col_fin_tip, u);

    // Fin ray overlay
    float ray = ray_mask(v_uv);
    col.rgb   = mix(col_fin_ray.rgb, col.rgb, ray);
    col.a    *= mix(col_fin_ray.a,   col.a,   ray);
    col.a    *= mix(1.0, 0.55, u);   // tips fade out more

    ALBEDO    = sat(col.rgb, saturation);
    ALPHA     = col.a;
    ROUGHNESS = roughness;
    METALLIC  = 0.0;
}

void light() {
    float NdotL = clamp(dot(NORMAL, LIGHT), 0.0, 1.0) * ATTENUATION;

    vec3 cel;
    if (NdotL < band1 + band_soft)
        cel = mix(col_fin_base.rgb * 0.5, col_fin_base.rgb * 0.75,
                  smoothstep(band1 - band_soft, band1 + band_soft, NdotL));
    else if (NdotL < band2 + band_soft)
        cel = mix(col_fin_base.rgb * 0.75, col_fin_base.rgb,
                  smoothstep(band2 - band_soft, band2 + band_soft, NdotL));
    else
        cel = mix(col_fin_base.rgb, col_fin_tip.rgb,
                  smoothstep(band3 - band_soft, band3 + band_soft, NdotL));

    DIFFUSE_LIGHT += sat(cel, saturation) * LIGHT_COLOR;
}
