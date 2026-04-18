shader_type spatial;
render_mode cull_back, depth_draw_opaque, diffuse_toon, specular_toon;

// ── Animation uniforms (written by FishAnimator after each compute dispatch) ─
uniform float body_phase    : hint_range(-6.28318, 6.28318) = 0.0;
uniform float body_amp      : hint_range(0.0, 0.5)          = 0.05;
uniform float body_freq     : hint_range(0.0, 6.0)          = 1.2;
uniform float tail_amp      : hint_range(0.0, 1.0)          = 0.11;
uniform float bank_angle    : hint_range(-45.0, 45.0)       = 0.0;
uniform float bob_offset    : hint_range(-0.5, 0.5)         = 0.0;

// Body region boundaries (UV.x = 0 snout → 1 peduncle)
uniform float head_end      : hint_range(0.05, 0.45) = 0.18;
uniform float tail_start    : hint_range(0.50, 0.95) = 0.68;

// Wave shape
uniform float head_flex     : hint_range(0.0, 0.3)  = 0.02;
uniform float tail_flex     : hint_range(0.5, 2.5)  = 1.25;
uniform float wave_freq     : hint_range(0.1, 12.0) = 2.2;   // spatial cycles

// ── Colour palette ────────────────────────────────────────────────────────────
uniform vec4 col_deep_shadow : source_color = vec4(0.55, 0.18, 0.02, 1.0);
uniform vec4 col_shadow      : source_color = vec4(0.82, 0.38, 0.04, 1.0);
uniform vec4 col_mid         : source_color = vec4(1.00, 0.62, 0.08, 1.0);
uniform vec4 col_highlight   : source_color = vec4(1.00, 0.90, 0.55, 1.0);
uniform vec4 col_belly       : source_color = vec4(1.00, 0.92, 0.72, 1.0);
uniform vec4 col_specular    : source_color = vec4(1.00, 0.98, 0.90, 1.0);
uniform vec4 col_rim         : source_color = vec4(1.00, 0.75, 0.30, 1.0);

// ── Cel shading thresholds ────────────────────────────────────────────────────
uniform float band1          : hint_range(0.0, 0.4)   = 0.08;
uniform float band2          : hint_range(0.0, 0.8)   = 0.32;
uniform float band3          : hint_range(0.0, 1.0)   = 0.65;
uniform float band_soft      : hint_range(0.001, 0.1) = 0.015;
uniform float spec_threshold : hint_range(0.80, 1.0)  = 0.92;
uniform float spec_hard      : hint_range(0.001, 0.05)= 0.012;
uniform float rim_power      : hint_range(1.0, 8.0)   = 3.5;
uniform float rim_strength   : hint_range(0.0, 4.0)   = 1.2;
uniform float saturation     : hint_range(0.0, 3.0)   = 1.45;

// Instance tint (per-fish colour variety via INSTANCE_CUSTOM.g)
uniform float instance_tint  : hint_range(0.0, 1.0)   = 0.25;

// Scale pattern
uniform float scale_density  : hint_range(4.0, 40.0)  = 18.0;
uniform float scale_contrast : hint_range(0.0, 0.4)   = 0.12;

// ── Iridescence ───────────────────────────────────────────────────────────────
uniform float iridescence    : hint_range(0.0, 1.0)   = 0.18;

varying vec2  v_uv;
varying float v_body_u;
varying float v_instance_tint_val;
varying vec3  v_normal_ws;
varying vec3  v_pos_ws;

// Lateral displacement envelope: 0 at head, rising through body, max at tail
float wave_envelope(float u) {
    float head_mask = smoothstep(head_end * 0.3, head_end, u);
    float body_ramp = mix(head_flex, 1.0, smoothstep(head_end, tail_start, u));
    float tail_ramp = mix(1.0, tail_flex, smoothstep(tail_start, 1.0, u));
    float region    = (u < tail_start) ? body_ramp : tail_ramp;
    return head_mask * region;
}

void vertex() {
    float u = UV.x;   // 0 = snout, 1 = peduncle
    v_uv = UV;
    v_body_u = u;
    v_instance_tint_val = INSTANCE_CUSTOM.g;

    // Travelling sine wave: phase propagates nose → tail
    float wave = sin(u * wave_freq * TAU - body_phase) * body_amp * wave_envelope(u);

    // Tail gets the full tail_amp multiplier already baked into the envelope,
    // but body_amp is the base; final displacement is wave (already scaled).
    VERTEX.x += wave;

    // Bank (roll) — rotate around Z in local space
    float bank_rad = radians(bank_angle);
    float cx = cos(bank_rad), sx = sin(bank_rad);
    vec3 v = VERTEX;
    VERTEX.y = v.y * cx - v.x * sx;
    VERTEX.x = v.y * sx + v.x * cx;

    // Vertical bob offset (idle animation)
    VERTEX.y += bob_offset;

    // Pass world-space data to fragment
    v_pos_ws    = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    v_normal_ws = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

// ── Scale pattern in UV space ─────────────────────────────────────────────────
float scale_pattern(vec2 uv) {
    // Hexagonal tiling approximation using two offset grids
    vec2 q  = uv * vec2(scale_density, scale_density * 0.6);
    vec2 q2 = q + vec2(0.5, 0.3);
    float g1 = fract(q.x) + fract(q.y);
    float g2 = fract(q2.x) + fract(q2.y);
    float sc = min(abs(g1 - 1.0), abs(g2 - 1.0));
    return smoothstep(0.02, 0.12, sc);   // 0 = scale edge, 1 = scale centre
}

// ── Saturation boost ──────────────────────────────────────────────────────────
vec3 sat(vec3 c, float s) {
    float lum = dot(c, vec3(0.213, 0.715, 0.072));
    return mix(vec3(lum), c, s);
}

void fragment() {
    // ── Belly / back gradient ─────────────────────────────────────────────────
    float vert = v_uv.y;   // 0=belly 1=back
    vec4 base_col = mix(col_belly, col_mid, smoothstep(0.25, 0.55, vert));

    // ── Scale overlay ─────────────────────────────────────────────────────────
    float sc = scale_pattern(v_uv);
    base_col.rgb *= (1.0 - scale_contrast * (1.0 - sc));

    // ── Instance hue tint (INSTANCE_CUSTOM.g carries tint strength) ───────────
    float tint = v_instance_tint_val * instance_tint;
    base_col.rgb = mix(base_col.rgb, base_col.rgb * 1.2, tint);

    // ── Iridescence (view-angle colour shift) ─────────────────────────────────
    float fresnel = pow(1.0 - clamp(dot(v_normal_ws, normalize(CAMERA_POSITION_WORLD - v_pos_ws)), 0.0, 1.0), 2.5);
    vec3 irid_col = vec3(0.8, 1.0, 0.6) * fresnel * iridescence;
    base_col.rgb += irid_col;

    ALBEDO    = sat(base_col.rgb, saturation);
    ROUGHNESS = 0.35;
    METALLIC  = 0.0;
}

void light() {
    // ── Diffuse NdotL → cel bands ─────────────────────────────────────────────
    float NdotL = clamp(dot(NORMAL, LIGHT), 0.0, 1.0) * ATTENUATION;

    vec3 cel;
    if (NdotL < band1 + band_soft)
        cel = mix(col_deep_shadow.rgb, col_shadow.rgb, smoothstep(band1 - band_soft, band1 + band_soft, NdotL));
    else if (NdotL < band2 + band_soft)
        cel = mix(col_shadow.rgb, col_mid.rgb, smoothstep(band2 - band_soft, band2 + band_soft, NdotL));
    else if (NdotL < band3 + band_soft)
        cel = mix(col_mid.rgb, col_highlight.rgb, smoothstep(band3 - band_soft, band3 + band_soft, NdotL));
    else
        cel = col_highlight.rgb;

    DIFFUSE_LIGHT += sat(cel, saturation) * LIGHT_COLOR;

    // ── Specular (hard dot, Blinn-Phong) ─────────────────────────────────────
    float NdotH    = clamp(dot(NORMAL, normalize(LIGHT + VIEW)), 0.0, 1.0);
    float spec_cel = smoothstep(spec_threshold - spec_hard, spec_threshold + spec_hard, NdotH);
    SPECULAR_LIGHT += col_specular.rgb * spec_cel * LIGHT_COLOR * ATTENUATION;

    // ── Rim / Fresnel ─────────────────────────────────────────────────────────
    float NdotV = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
    float rim   = pow(1.0 - NdotV, rim_power) * rim_strength;
    SPECULAR_LIGHT += col_rim.rgb * rim * LIGHT_COLOR * ATTENUATION;
}
