// ─────────────────────────────────────────────────────────────────────────────
// fish_outline.gdshader
// Inverted-hull outline pass — set as MaterialOverride.NextPass on the toon mat.
//
// Classic technique: cull_front so only back-faces render; expand verts along
// normals to create a silhouette slightly larger than the fish body.
//
// The wave deformation is replicated here so the outline stays glued to the
// animated body.  Keep wave params in sync with fish_toon.gdshader.
// ─────────────────────────────────────────────────────────────────────────────

shader_type spatial;
render_mode cull_front, unshaded, depth_draw_opaque, blend_mix;

// ── Outline appearance ────────────────────────────────────────────────────────
// outline_width is in LOCAL units — scale it if your fish mesh is not unit-scale.
uniform float outline_width : hint_range(0.0, 0.08) = 0.022;
uniform vec4  outline_color : source_color = vec4(0.01, 0.04, 0.14, 1.0);

// ── Wave params — must match fish_toon.gdshader ───────────────────────────────
uniform float head_end       : hint_range(0.0, 0.5)  = 0.18;
uniform float tail_start     : hint_range(0.5, 1.0)  = 0.62;
uniform float wave_frequency : hint_range(0.1, 12.0) = 2.5;
uniform float wave_amplitude : hint_range(0.0, 0.6)  = 0.13;
uniform float wave_speed     : hint_range(0.0, 20.0) = 5.0;
uniform float head_flex      : hint_range(0.0, 0.3)  = 0.02;
uniform float tail_flex      : hint_range(0.5, 2.5)  = 1.05;

void vertex() {
    float t = UV.x;

    float flex;
    float inv_head = 1.0 / max(head_end, 0.001);
    float inv_body = 1.0 / max(tail_start - head_end, 0.001);
    float inv_tail = 1.0 / max(1.0 - tail_start, 0.001);

    if (t < head_end) {
        flex = head_flex * (t * inv_head);
    } else if (t > tail_start) {
        flex = mix(1.0, tail_flex, (t - tail_start) * inv_tail);
    } else {
        flex = mix(head_flex, 1.0, (t - head_end) * inv_body);
    }

    float phase = INSTANCE_CUSTOM.r * TAU;
    float wave  = sin(TIME * wave_speed - t * wave_frequency * TAU + phase)
                  * wave_amplitude * flex;
    VERTEX.x += wave;

    // Expand along smooth normals for a clean silhouette.
    VERTEX += NORMAL * outline_width;
}

void fragment() {
    ALBEDO = outline_color.rgb;
    ALPHA  = outline_color.a;
}
