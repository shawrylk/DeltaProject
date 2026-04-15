using System;
using DeltaProject.Domain;
using DeltaProject.Domain.Interfaces;
using DeltaProject.Infrastructure.Animation;
using DeltaProject.Infrastructure.Rendering;
using Godot;

public partial class AnimPreviewPanel : CanvasLayer
{
    private IAnimSimulation _sim    = null!;
    private AnimState       _state;
    private float           _elapsed;

    private SubViewport    _viewport = null!;
    private MeshInstance3D _mesh     = null!;
    private ShaderMaterial _bodyMat  = null!;
    private readonly ShaderMaterial[] _finMats = new ShaderMaterial[4];

    private Camera3D _cam     = null!;
    private float    _yaw     = 0.4f;
    private float    _pitch   = 0.25f;
    private float    _camDist = 1.8f;
    private bool     _dragging;

    private HSlider      _sldSpeed   = null!;
    private HSlider      _sldTurn    = null!;
    private HSlider      _sldCurrent = null!;
    private OptionButton _stateOpt   = null!;
    private CheckButton  _autoBtn    = null!;
    private Control      _panel      = null!;

    private readonly Label[] _stateLabels = new Label[10];
    private static readonly string[] StateNames =
    {
        "BodyPhase", "BodyAmp", "BodyFreq",   "TailAmp",    "DorsalPhase",
        "PecL",      "PecR",    "MouthOpen",  "BankAngle",  "BobPhase",
    };

    public override void _Ready()
    {
        Layer = 11;   // above FishUI (Layer=10)
        try   { _sim = new GpuAnimSimulation(); }
        catch (Exception ex)
        {
            GD.Print($"AnimPreviewPanel: GPU unavailable ({ex.Message}) — CPU fallback.");
            _sim = new CpuAnimSimulation();
        }
        BuildViewport();
        BuildUI();
    }

    public override void _ExitTree() => _sim.Dispose();

    // ── Viewport setup ────────────────────────────────────────────────────────

    private void BuildViewport()
    {
        _viewport = new SubViewport
        {
            Size                   = new Vector2I(300, 260),
            OwnWorld3D             = true,   // isolated world — no swarm, no main lights
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(_viewport);

        var dir = new DirectionalLight3D { LightColor = new Color(1f, 0.96f, 0.88f), LightEnergy = 1.6f };
        dir.RotateObjectLocal(Vector3.Right, -0.8f);
        dir.RotateObjectLocal(Vector3.Up,     0.4f);
        _viewport.AddChild(dir);

        _viewport.AddChild(new OmniLight3D
        {
            LightColor  = new Color(0.2f, 0.55f, 1f),
            LightEnergy = 0.5f,
            OmniRange   = 8f,
            Position    = new Vector3(0f, -1.5f, 0f),
        });

        _mesh = new MeshInstance3D { Mesh = GoldfishMeshBuilder.Build() };
        _viewport.AddChild(_mesh);

        var bodyShader = GD.Load<Shader>("res://Shaders/fish_body.gdshader");
        var finShader  = GD.Load<Shader>("res://Shaders/fish_fins.gdshader");
        _bodyMat = new ShaderMaterial { Shader = bodyShader };
        _mesh.SetSurfaceOverrideMaterial(0, _bodyMat);
        for (int i = 0; i < 4; i++)
        {
            _finMats[i] = new ShaderMaterial { Shader = finShader };
            _mesh.SetSurfaceOverrideMaterial(i + 1, _finMats[i]);
        }

        _cam = new Camera3D { Fov = 50f };
        _viewport.AddChild(_cam);
        UpdateCamera();
    }

    // ── UI layout ─────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        var toggleBtn = new Button { Text = "▶ Anim Preview" };
        toggleBtn.SetAnchor(Side.Left, 1f); toggleBtn.SetAnchor(Side.Right,  1f);
        toggleBtn.SetAnchor(Side.Top,  0f); toggleBtn.SetAnchor(Side.Bottom, 0f);
        toggleBtn.OffsetLeft = -115f; toggleBtn.OffsetRight  = -5f;
        toggleBtn.OffsetTop  =    5f; toggleBtn.OffsetBottom = 30f;
        toggleBtn.Pressed += () => _panel.Visible = !_panel.Visible;
        AddChild(toggleBtn);

        _panel = new PanelContainer();
        _panel.SetAnchor(Side.Left, 1f); _panel.SetAnchor(Side.Right,  1f);
        _panel.SetAnchor(Side.Top,  0f); _panel.SetAnchor(Side.Bottom, 1f);
        _panel.OffsetLeft = -330f; _panel.OffsetRight = 0f;
        AddChild(_panel);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _panel.AddChild(scroll);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(vbox);

        // SubViewport container
        var svc = new SubViewportContainer
        {
            Stretch             = true,
            CustomMinimumSize   = new Vector2(300, 260),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        svc.AddChild(_viewport);
        svc.GuiInput += OnViewportInput;
        vbox.AddChild(svc);

        // AnimInput sliders
        vbox.AddChild(SectionLabel("ANIM INPUT"));
        _sldSpeed   = SliderRow(vbox, "Speed",       0f, 1f,  0.5f, 0.01f);
        _sldTurn    = SliderRow(vbox, "Turn Rate",  -1f, 1f,  0f,   0.01f);
        _sldCurrent = SliderRow(vbox, "Water Curr.", 0f, 1f,  0.4f, 0.01f);

        var stateRow = new HBoxContainer();
        stateRow.AddChild(new Label { Text = "State", CustomMinimumSize = new Vector2(90, 0) });
        _stateOpt = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var n in Enum.GetNames<FishState>()) _stateOpt.AddItem(n);
        stateRow.AddChild(_stateOpt);
        vbox.AddChild(stateRow);

        _autoBtn = new CheckButton { Text = "Auto-Animate" };
        vbox.AddChild(_autoBtn);

        // AnimState read-outs
        vbox.AddChild(SectionLabel("ANIM STATE"));
        for (int i = 0; i < StateNames.Length; i++)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = StateNames[i], CustomMinimumSize = new Vector2(100, 0) });
            _stateLabels[i] = new Label { Text = "0.000" };
            row.AddChild(_stateLabels[i]);
            vbox.AddChild(row);
        }
    }

    // ── Per-frame ─────────────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (!_panel.Visible) return;
        _elapsed += (float)delta;

        float speed, turnRate;
        if (_autoBtn.ButtonPressed)
        {
            speed    = 0.5f + 0.5f * MathF.Sin(_elapsed * 0.4f);
            turnRate = 1.2f        * MathF.Sin(_elapsed * 0.7f);
        }
        else
        {
            speed    = (float)_sldSpeed.Value;
            turnRate = (float)_sldTurn.Value;
        }

        var inp = new AnimInput
        {
            Speed        = speed,
            TurnRate     = turnRate,
            WaterCurrent = (float)_sldCurrent.Value,
            Elapsed      = _elapsed,
            DeltaTime    = (float)delta,
            State        = (FishState)_stateOpt.Selected,
        };

        _state = _sim.Step(in inp);
        PushUniforms();

        float[] vals =
        {
            _state.BodyPhase, _state.BodyAmp,  _state.BodyFreq, _state.TailAmp, _state.DorsalPhase,
            _state.PecL,      _state.PecR,     _state.MouthOpen, _state.BankAngle, _state.BobPhase,
        };
        for (int i = 0; i < vals.Length; i++) _stateLabels[i].Text = vals[i].ToString("F3");
    }

    // ── Shader uniforms — mirrors FishAnimator.PushUniforms ───────────────────

    private void PushUniforms()
    {
        _bodyMat.SetShaderParameter("body_phase", _state.BodyPhase);
        _bodyMat.SetShaderParameter("body_amp",   _state.BodyAmp);
        _bodyMat.SetShaderParameter("body_freq",  _state.BodyFreq);
        _bodyMat.SetShaderParameter("tail_amp",   _state.TailAmp);
        _bodyMat.SetShaderParameter("bank_angle", _state.BankAngle);
        _bodyMat.SetShaderParameter("bob_offset", MathF.Sin(_state.BobPhase) * 0.04f);

        float[] pec = { 0f, 0f, _state.PecL, _state.PecR };
        for (int i = 0; i < 4; i++)
        {
            _finMats[i].SetShaderParameter("body_phase",   _state.BodyPhase);
            _finMats[i].SetShaderParameter("body_amp",     _state.BodyAmp);
            _finMats[i].SetShaderParameter("dorsal_phase", _state.DorsalPhase);
            _finMats[i].SetShaderParameter("tail_amp",     _state.TailAmp);
            _finMats[i].SetShaderParameter("bank_angle",   _state.BankAngle);
            _finMats[i].SetShaderParameter("pec_angle",    pec[i]);
        }
    }

    // ── Camera orbit ──────────────────────────────────────────────────────────

    private void OnViewportInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb)
        {
            if      (mb.ButtonIndex == MouseButton.Left)       _dragging = mb.Pressed;
            else if (mb.ButtonIndex == MouseButton.WheelUp)    { _camDist = Mathf.Max(0.8f, _camDist - 0.15f); UpdateCamera(); }
            else if (mb.ButtonIndex == MouseButton.WheelDown)  { _camDist = Mathf.Min(5f,   _camDist + 0.15f); UpdateCamera(); }
        }
        else if (ev is InputEventMouseMotion mm && _dragging)
        {
            _yaw   -= mm.Relative.X * 0.008f;
            _pitch  = Mathf.Clamp(_pitch - mm.Relative.Y * 0.008f, -1.2f, 1.2f);
            UpdateCamera();
        }
    }

    private void UpdateCamera()
    {
        float x = _camDist * MathF.Cos(_pitch) * MathF.Sin(_yaw);
        float y = _camDist * MathF.Sin(_pitch);
        float z = _camDist * MathF.Cos(_pitch) * MathF.Cos(_yaw);
        _cam.Position = new Vector3(x, y, z);
        _cam.LookAt(Vector3.Zero, Vector3.Up);
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private static Label SectionLabel(string text)
    {
        var lbl = new Label { Text = text };
        lbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.85f, 1f));
        return lbl;
    }

    private static HSlider SliderRow(VBoxContainer parent, string label,
                                      float min, float max, float initial, float step)
    {
        var row    = new HBoxContainer();
        var lbl    = new Label { Text = label, CustomMinimumSize = new Vector2(90, 0) };
        var slider = new HSlider { MinValue = min, MaxValue = max, Value = initial,
                                    Step = step, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var valLbl = new Label { Text = initial.ToString("F2"), CustomMinimumSize = new Vector2(46, 0) };
        slider.ValueChanged += v => valLbl.Text = ((float)v).ToString("F2");
        row.AddChild(lbl); row.AddChild(slider); row.AddChild(valLbl);
        parent.AddChild(row);
        return slider;
    }
}
