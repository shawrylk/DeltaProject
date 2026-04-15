#[compute]
#version 450

// One thread integrates one fish's animation state per frame.
layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

#define TAU 6.28318530718

// ─── Binding 0: per-frame inputs from FishController (UBO, read-only) ────────
// std140 layout — all offsets match FishAnimator.AnimInput exactly.
layout(set = 0, binding = 0, std140) uniform AnimInput {
    float speed;       //  0  normalised 0-1
    float turn_rate;   //  4  signed rad/s (+ = left)
    float water_curr;  //  8  current strength
    float elapsed;     // 12  total time since start
    float dt;          // 16  delta time
    int   state;       // 20  0=idle 1=wander 2=seek 3=eat
    float pad0;        // 24
    float pad1;        // 28
} inp;                 // total 32 B

// ─── Binding 1: persistent animation state (SSBO, read+write) ────────────────
// FishAnimator reads this buffer back after each dispatch and pushes the values
// to the mesh shader as uniforms.  std430 — plain float array, no alignment gaps.
layout(set = 0, binding = 1, std430) buffer AnimState {
    float body_phase;    //  0  accumulated body-wave phase (rad)
    float body_amp;      //  4  current lateral amplitude
    float body_freq;     //  8  current wave frequency (Hz)
    float tail_amp;      // 12  tail tip amplitude (> body)
    float dorsal_phase;  // 16  dorsal flutter phase
    float pec_L;         // 20  left pectoral angle (deg)
    float pec_R;         // 24  right pectoral angle (deg)
    float mouth_open;    // 28  0=closed 1=open
    float bank_angle;    // 32  roll into turn (deg)
    float bob_phase;     // 36  idle vertical bob phase
} st;                    // total 40 B

// Smooth a value toward a target with an exponential approach.
float approach(float cur, float tgt, float rate) {
    return cur + (tgt - cur) * clamp(rate * inp.dt, 0.0, 1.0);
}

void main() {
    float spd = inp.speed;
    float dt  = inp.dt;

    // ── Body wave: frequency and amplitude scale with speed ───────────────────
    // Idle: gentle breath-like undulation (0.8 Hz, small).
    // Full speed: rapid propulsion beat (4.3 Hz, large).
    float tgt_freq = 0.8  + spd * 3.5;
    float tgt_amp  = 0.02 + spd * 0.13;

    st.body_freq = approach(st.body_freq, tgt_freq, 3.0);
    st.body_amp  = approach(st.body_amp,  tgt_amp,  4.0);
    st.body_phase += st.body_freq * TAU * dt;   // integrate phase

    // Tail beats more than the body (lever-arm amplification).
    st.tail_amp = st.body_amp * 2.2;

    // ── Dorsal fin: higher-frequency flutter, proportional to speed ───────────
    float dors_freq = 2.0 + spd * 2.5;
    st.dorsal_phase += dors_freq * TAU * dt;

    // ── Pectoral fins: spring-damper toward target angle ─────────────────────
    // At rest: spread open (neutral, 0°).
    // At speed: fold back (−30°).
    // Turning: inside fin tucks, outside fin spreads for steering.
    float pec_fold = -30.0 * spd;
    float pec_steer = inp.turn_rate * 20.0;   // rad/s → degrees of spread
    st.pec_L = approach(st.pec_L, pec_fold + pec_steer, 5.0);
    st.pec_R = approach(st.pec_R, pec_fold - pec_steer, 5.0);

    // ── Mouth: open during eat state (state == 3) ─────────────────────────────
    float tgt_mouth = (inp.state == 3) ? 1.0 : 0.0;
    st.mouth_open = approach(st.mouth_open, tgt_mouth, 6.0);

    // ── Body bank (roll into turns) ───────────────────────────────────────────
    float tgt_bank = inp.turn_rate * 18.0;
    st.bank_angle = approach(st.bank_angle, tgt_bank, 4.0);

    // ── Idle bob: very slow vertical oscillation, suppressed at speed ─────────
    float bob_freq = 0.5 + spd * 0.3;
    st.bob_phase += bob_freq * TAU * dt;
}
