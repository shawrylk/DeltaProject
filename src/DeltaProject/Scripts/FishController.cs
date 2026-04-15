using DeltaProject.Domain;
using Godot;
using System;

/// <summary>
/// Custom physics for one goldfish: buoyancy, drag, thrust, water current.
/// State machine: Idle → Wander → SeekFood → Eat.
/// Exposes NormalisedSpeed, TurnRate, State for FishAnimator.
/// </summary>
public partial class FishController : Node3D
{

    // ── Exports ───────────────────────────────────────────────────────────────
    [Export] public NodePath FoodManagerPath = new NodePath("../FoodManager");
    [Export] public Vector3  TankHalfExtents = new Vector3(5f, 2f, 5f);

    [ExportGroup("Physics")]
    [Export] public float MaxSpeed        = 3.5f;
    [Export] public float Drag            = 1.8f;     // linear drag coefficient
    [Export] public float BuoyancySpring  = 6f;       // restoring force toward rest depth
    [Export] public float BuoyancyDamp    = 2.5f;
    [Export] public float RestDepth       = -0.3f;    // preferred Y relative to tank centre
    [Export] public float ThrustForce     = 5f;
    [Export] public float TurnSpeed       = 2.5f;     // rad/s max yaw rate
    [Export] public float WaterCurrentStr = 0.4f;     // strength of ambient current

    [ExportGroup("AI")]
    [Export] public float WanderInterval  = 4f;       // pick new target every N seconds
    [Export] public float HungerRate      = 0.04f;    // hunger increase per second
    [Export] public float HungerThreshold = 0.55f;    // hunger level that triggers SeekFood
    [Export] public float EatDistance     = 0.35f;    // how close to snap up food
    [Export] public float EatDuration     = 1.2f;     // seconds mouth stays open

    // ── Public read-outs for FishAnimator ─────────────────────────────────────
    public float     NormalisedSpeed    { get; private set; }
    public float     TurnRate           { get; private set; }   // signed rad/s
    public float     WaterCurrentStrength => WaterCurrentStr;
    public FishState State              { get; private set; } = FishState.Idle;

    // ── Internal ──────────────────────────────────────────────────────────────
    private Vector3 _velocity      = Vector3.Zero;
    private float   _hunger        = 0f;
    private float   _wanderTimer   = 0f;
    private float   _eatTimer      = 0f;
    private Vector3 _wanderTarget  = Vector3.Zero;
    private FoodManager? _food;

    // Slow sinusoidal current that varies over time
    private float _currentAngle = 0f;

    public override void _Ready()
    {
        _food = GetNodeOrNull<FoodManager>(FoodManagerPath);
        PickWanderTarget();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _hunger = Math.Min(1f, _hunger + HungerRate * dt);
        _currentAngle += dt * 0.18f;

        UpdateState();
        Vector3 steer = ComputeSteer(dt);
        Integrate(steer, dt);
        AlignToVelocity(dt);
    }

    // ── State machine ─────────────────────────────────────────────────────────

    private void UpdateState()
    {
        switch (State)
        {
            case FishState.Idle:
            case FishState.Wander:
                if (_hunger >= HungerThreshold && _food?.HasFood() == true)
                    State = FishState.SeekFood;
                else
                    UpdateWander();
                break;

            case FishState.SeekFood:
                if (_food == null || !_food.HasFood())
                    State = FishState.Wander;
                else if (_food.NearestDistance(GlobalPosition) <= EatDistance)
                    StartEat();
                break;

            case FishState.Eat:
                _eatTimer -= (float)GetPhysicsProcessDeltaTime();
                if (_eatTimer <= 0f) State = FishState.Wander;
                break;
        }
    }

    private void UpdateWander()
    {
        _wanderTimer -= (float)GetPhysicsProcessDeltaTime();
        float distSq = GlobalPosition.DistanceSquaredTo(_wanderTarget);
        if (_wanderTimer <= 0f || distSq < 0.5f)
        {
            PickWanderTarget();
            State = FishState.Wander;
        }
        else if (distSq < 0.1f)
            State = FishState.Idle;
    }

    private void StartEat()
    {
        _food!.ConsumeNearest(GlobalPosition);
        _hunger   = 0f;
        _eatTimer = EatDuration;
        State     = FishState.Eat;
    }

    // ── Steering ──────────────────────────────────────────────────────────────

    private Vector3 ComputeSteer(float dt)
    {
        Vector3 goal = State switch
        {
            FishState.SeekFood => _food?.NearestPosition(GlobalPosition) ?? _wanderTarget,
            FishState.Eat      => GlobalPosition,   // hover in place
            _                  => _wanderTarget,
        };

        // Desired direction
        Vector3 toGoal = goal - GlobalPosition;
        float   dist   = toGoal.Length();
        if (dist < 0.01f) return Vector3.Zero;

        Vector3 desired = toGoal / dist * MaxSpeed;

        // Speed scalar: slow near goal, no reverse thrust
        float speedScale = Math.Clamp(dist * 0.5f, 0.1f, 1f);
        if (State == FishState.Idle) speedScale = 0.05f;

        // Thrust along forward axis only
        Vector3 forward   = -GlobalTransform.Basis.Z;
        float   alignment = forward.Dot(desired.Normalized());
        float   thrust    = ThrustForce * speedScale * Math.Max(0f, alignment);
        return forward * thrust;
    }

    // ── Physics integration ───────────────────────────────────────────────────

    private void Integrate(Vector3 thrust, float dt)
    {
        // Buoyancy spring toward rest depth
        float   yError  = (RestDepth - GlobalPosition.Y);
        float   yForce  = yError * BuoyancySpring - _velocity.Y * BuoyancyDamp;

        // Ambient water current (slow sinusoidal drift)
        Vector3 current = new Vector3(
            MathF.Sin(_currentAngle) * WaterCurrentStr,
            0f,
            MathF.Cos(_currentAngle * 0.7f) * WaterCurrentStr * 0.6f);

        // Drag (quadratic, opposing velocity)
        Vector3 drag = -_velocity * Drag;

        _velocity += (thrust + drag + new Vector3(0, yForce, 0) + current) * dt;
        _velocity  = _velocity.LimitLength(MaxSpeed);

        GlobalPosition += _velocity * dt;

        // Soft boundary clamp
        GlobalPosition = new Vector3(
            SoftClamp(GlobalPosition.X, TankHalfExtents.X),
            SoftClamp(GlobalPosition.Y, TankHalfExtents.Y),
            SoftClamp(GlobalPosition.Z, TankHalfExtents.Z));

        // Expose normalised speed and turn rate for animator
        NormalisedSpeed = _velocity.Length() / MaxSpeed;
    }

    private static float SoftClamp(float v, float half)
    {
        float margin = half * 0.15f;
        if (v >  half - margin) return Mathf.Lerp(v, half - margin, 0.15f);
        if (v < -half + margin) return Mathf.Lerp(v, -half + margin, 0.15f);
        return v;
    }

    // ── Rotation: align -Z forward to velocity ────────────────────────────────

    private void AlignToVelocity(float dt)
    {
        Vector3 vel2d = new Vector3(_velocity.X, 0f, _velocity.Z);
        if (vel2d.LengthSquared() < 0.001f) { TurnRate = 0f; return; }

        Vector3 desiredFwd = -vel2d.Normalized();   // -Z = Godot forward
        Vector3 currentFwd = -GlobalTransform.Basis.Z;

        float angle = currentFwd.SignedAngleTo(desiredFwd, Vector3.Up);
        float maxA  = TurnSpeed * dt;
        float step  = Math.Clamp(angle, -maxA, maxA);

        TurnRate = step / dt;   // rad/s for animator

        GlobalRotation = new Vector3(
            GlobalRotation.X,
            GlobalRotation.Y + step,
            GlobalRotation.Z);

        // Pitch toward velocity Y
        float targetPitch = -MathF.Atan2(_velocity.Y, vel2d.Length()) * 0.4f;
        GlobalRotation = new Vector3(
            Mathf.Lerp(GlobalRotation.X, targetPitch, dt * 4f),
            GlobalRotation.Y,
            GlobalRotation.Z);
    }

    // ── Wander targeting ──────────────────────────────────────────────────────

    private void PickWanderTarget()
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        _wanderTarget = new Vector3(
            rng.RandfRange(-TankHalfExtents.X * 0.75f, TankHalfExtents.X * 0.75f),
            RestDepth + rng.RandfRange(-0.6f, 0.6f),
            rng.RandfRange(-TankHalfExtents.Z * 0.75f, TankHalfExtents.Z * 0.75f));
        _wanderTimer = rng.RandfRange(WanderInterval * 0.5f, WanderInterval * 1.5f);
    }
}
