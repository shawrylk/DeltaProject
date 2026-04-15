#[compute]
#version 450

// ─── Thread group size ────────────────────────────────────────────────────────
// 64 threads per group — keep fish count a multiple of 64 for full occupancy.
layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

// ─── Data types ───────────────────────────────────────────────────────────────
// 32 bytes per fish (vec3 + float pad, repeated).  std430 packs vec3 to 12 B,
// but we add explicit padding so C# StructLayout can mirror this exactly.
struct FishData {
    vec3  position; // 12 B
    float pad0;     //  4 B  → 16 B offset
    vec3  velocity; // 12 B
    float pad1;     //  4 B  → 32 B total
};

// ─── Binding 0 — fish SSBO (read + write) ────────────────────────────────────
layout(set = 0, binding = 0, std430) restrict buffer FishBuffer {
    FishData fish[];
} fishBuffer;

// ─── Binding 1 — simulation params UBO (read-only) ───────────────────────────
// std140 layout: all scalars are 4-byte aligned; vec4 is 16-byte aligned.
// The C# SimParams struct uses FieldOffset to mirror this exactly.
layout(set = 0, binding = 1, std140) uniform SimParams {
    uint  fishCount;          // offset  0
    float deltaTime;          // offset  4
    float separationRadius;   // offset  8
    float alignmentRadius;    // offset 12
    float cohesionRadius;     // offset 16
    float separationWeight;   // offset 20
    float alignmentWeight;    // offset 24
    float cohesionWeight;     // offset 28
    float maxSpeed;           // offset 32
    float minSpeed;           // offset 36
    float boundaryMargin;     // offset 40
    float _pad;               // offset 44  (align vec4 to 48)
    vec4  boundsHalfExtents;  // offset 48  xyz = half-extents, w unused
} params;

// ─── Helpers ──────────────────────────────────────────────────────────────────

// Safe normalise — returns zero vector if length is below epsilon.
vec3 safeNorm(vec3 v) {
    float len = length(v);
    return len > 0.0001 ? v / len : vec3(0.0);
}

// Smooth soft-boundary repulsion: returns a push force toward origin
// when |coord| is within `margin` of the half-extent.
float boundaryForce(float pos, float halfExt, float margin) {
    float dist = halfExt - abs(pos);
    if (dist >= margin) return 0.0;
    return -sign(pos) * (1.0 - dist / margin) * 4.0;
}

// ─── Kernel ───────────────────────────────────────────────────────────────────
void main() {
    uint index = gl_GlobalInvocationID.x;
    if (index >= params.fishCount) return;

    vec3 pos = fishBuffer.fish[index].position;
    vec3 vel = fishBuffer.fish[index].velocity;

    // Precompute squared radii for cheaper distance tests.
    float sepR2   = params.separationRadius * params.separationRadius;
    float alignR2 = params.alignmentRadius  * params.alignmentRadius;
    float cohR2   = params.cohesionRadius   * params.cohesionRadius;

    vec3 sepForce   = vec3(0.0);
    vec3 alignSum   = vec3(0.0);
    vec3 cohCenter  = vec3(0.0);
    uint sepCount   = 0u;
    uint alignCount = 0u;
    uint cohCount   = 0u;

    // O(n²) neighbour scan — fast enough for ≤ 4096 fish on modern GPUs.
    for (uint i = 0u; i < params.fishCount; i++) {
        if (i == index) continue;

        vec3  diff  = pos - fishBuffer.fish[i].position;
        float dist2 = dot(diff, diff);

        // Separation: steer away, weighted by inverse distance.
        if (dist2 < sepR2 && dist2 > 0.0001) {
            sepForce += safeNorm(diff) / sqrt(dist2);
            sepCount++;
        }
        // Alignment: match neighbours' velocity.
        if (dist2 < alignR2) {
            alignSum += fishBuffer.fish[i].velocity;
            alignCount++;
        }
        // Cohesion: steer toward local centre of mass.
        if (dist2 < cohR2) {
            cohCenter += fishBuffer.fish[i].position;
            cohCount++;
        }
    }

    // ── Accumulate steering forces ────────────────────────────────────────
    vec3 accel = vec3(0.0);

    if (sepCount > 0u)
        accel += (sepForce / float(sepCount)) * params.separationWeight;

    if (alignCount > 0u) {
        // Steer velocity toward the average heading of neighbours.
        vec3 desired = safeNorm(alignSum / float(alignCount)) * params.maxSpeed;
        accel += (desired - vel) * params.alignmentWeight * 0.15;
    }

    if (cohCount > 0u) {
        vec3 center = cohCenter / float(cohCount);
        accel += safeNorm(center - pos) * params.cohesionWeight;
    }

    // ── Soft boundary repulsion ───────────────────────────────────────────
    vec3 half = params.boundsHalfExtents.xyz;
    accel.x += boundaryForce(pos.x, half.x, params.boundaryMargin);
    accel.y += boundaryForce(pos.y, half.y, params.boundaryMargin);
    accel.z += boundaryForce(pos.z, half.z, params.boundaryMargin);

    // ── Integrate ─────────────────────────────────────────────────────────
    vel += accel * params.deltaTime;

    float speed = length(vel);
    if (speed > params.maxSpeed) vel = vel / speed * params.maxSpeed;
    if (speed < params.minSpeed) vel = vel / max(speed, 0.0001) * params.minSpeed;

    pos += vel * params.deltaTime;

    // Hard clamp as last resort (prevents NaN from propagating).
    pos = clamp(pos, -half, half);

    fishBuffer.fish[index].position = pos;
    fishBuffer.fish[index].velocity = vel;
}
