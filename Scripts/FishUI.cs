// ─────────────────────────────────────────────────────────────────────────────
// FishUI.cs
// Comprehensive runtime parameter panel for DeltaProject.
//
// Sections:
//   • Simulation   — fish count, bounds, speed, boundary margin, reinit button
//   • Boid Rules   — separation / alignment / cohesion radii + weights
//   • Body Regions — head_end, tail_start (UV.x boundaries)
//   • Wave Anim    — frequency, amplitude, speed, head/tail flex, phase offset
//   • Toon Shading — cel band thresholds, colours, specular, rim
//   • Visual       — outline width/colour, saturation, edge glow, instance tint
//
// Press Escape to toggle the panel.  Works at any screen size (responsive anchor).
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

public partial class FishUI : CanvasLayer
{
    [Export] public NodePath FishManagerPath  = "../FishManager";
    [Export] public NodePath FishRendererPath = "../FishRenderer";

    private FishManager  _fm  = null!;
    private FishRenderer _fr  = null!;

    private PanelContainer _panel = null!;
    private bool           _panelVisible = true;
    private Label          _fpsLabel     = null!;
    private Label          _modeLabel    = null!;

    // ─────────────────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        _fm = GetNode<FishManager>(FishManagerPath);
        _fr = GetNode<FishRenderer>(FishRendererPath);
        Layer = 10;
        BuildUI();
    }

    public override void _Process(double _delta)
    {
        if (Input.IsActionJustPressed("ui_cancel")) TogglePanel();
        _fpsLabel.Text = $"FPS: {Engine.GetFramesPerSecond():F0}  Fish: {_fm.FishCount}";
    }

    private void TogglePanel()
    {
        _panelVisible = !_panelVisible;
        _panel.Visible = _panelVisible;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UI construction
    // ─────────────────────────────────────────────────────────────────────────
    private void BuildUI()
    {
        // Outer panel — left strip, full height
        _panel = new PanelContainer();
        _panel.AnchorLeft   = 0; _panel.AnchorRight  = 0;
        _panel.AnchorTop    = 0; _panel.AnchorBottom = 1;
        _panel.OffsetRight  = 340;
        _panel.OffsetBottom = 0;
        AddChild(_panel);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _panel.AddChild(scroll);

        var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(vbox);

        // ── Header ───────────────────────────────────────────────────────────
        Heading(vbox, "DELTA PROJECT", new Color(0.40f, 0.85f, 1.0f));
        _fpsLabel  = SmallLabel(vbox, "FPS: –");
        _modeLabel = SmallLabel(vbox, "Press Esc to toggle panel", new Color(0.6f, 0.6f, 0.6f));
        Sep(vbox);

        // ── Simulation ───────────────────────────────────────────────────────
        SectionHeader(vbox, "Simulation");

        // Fish count — spinner, applied on Reinitialize
        var fishCountRow = IntRow(vbox, "Fish Count", 64, 8192, _fm.FishCount, 64,
            v => _fm.FishCount = v);

        SliderRow(vbox, "Bounds X (m)", 10, 200, _fm.BoundsSize.X, 1f,
            v => _fm.BoundsSize = new Vector3(v, _fm.BoundsSize.Y, _fm.BoundsSize.Z));
        SliderRow(vbox, "Bounds Y (m)", 5, 100, _fm.BoundsSize.Y, 1f,
            v => _fm.BoundsSize = new Vector3(_fm.BoundsSize.X, v, _fm.BoundsSize.Z));
        SliderRow(vbox, "Bounds Z (m)", 10, 200, _fm.BoundsSize.Z, 1f,
            v => _fm.BoundsSize = new Vector3(_fm.BoundsSize.X, _fm.BoundsSize.Y, v));
        SliderRow(vbox, "Min Speed",    0.5f, 10, _fm.MinSpeed,  0.1f, v => _fm.MinSpeed  = v);
        SliderRow(vbox, "Max Speed",    1f,   20, _fm.MaxSpeed,  0.1f, v => _fm.MaxSpeed  = v);
        SliderRow(vbox, "Bound Margin", 1f,   25, _fm.BoundaryMargin, 0.5f,
            v => _fm.BoundaryMargin = v);

        Button(vbox, "Reinitialize Simulation", new Color(1f, 0.55f, 0.1f), () =>
        {
            _fr.GetMeta("");  // just a no-op touch to suppress warning
            _fm.Reinitialize();
        });
        Sep(vbox);

        // ── Boid Rules ───────────────────────────────────────────────────────
        SectionHeader(vbox, "Boid Rules");
        SliderRow(vbox, "Separation R",  0.5f, 15, _fm.SeparationRadius, 0.1f, v => _fm.SeparationRadius = v);
        SliderRow(vbox, "Alignment R",   0.5f, 25, _fm.AlignmentRadius,  0.1f, v => _fm.AlignmentRadius  = v);
        SliderRow(vbox, "Cohesion R",    0.5f, 30, _fm.CohesionRadius,   0.1f, v => _fm.CohesionRadius   = v);
        SliderRow(vbox, "Sep Weight",    0f,    5, _fm.SeparationWeight, 0.05f, v => _fm.SeparationWeight = v);
        SliderRow(vbox, "Align Weight",  0f,    5, _fm.AlignmentWeight,  0.05f, v => _fm.AlignmentWeight  = v);
        SliderRow(vbox, "Cohes Weight",  0f,    5, _fm.CohesionWeight,   0.05f, v => _fm.CohesionWeight   = v);
        Sep(vbox);

        // ── Body Regions ─────────────────────────────────────────────────────
        SectionHeader(vbox, "Body Regions  (UV.x: 0 = snout  →  1 = tail)");
        InfoLabel(vbox, "Head 0→head_end  |  Body head_end→tail_start  |  Tail tail_start→1");
        SliderRow(vbox, "head_end",   0.05f, 0.45f, 0.18f, 0.01f,
            v => SyncWave(vbox, "head_end", v));
        SliderRow(vbox, "tail_start", 0.50f, 0.95f, 0.62f, 0.01f,
            v => SyncWave(vbox, "tail_start", v));
        Sep(vbox);

        // ── Wave Animation ───────────────────────────────────────────────────
        SectionHeader(vbox, "Wave Animation");
        SliderRow(vbox, "Frequency",    0.1f, 12f,  2.5f, 0.05f,
            v => SyncWave(vbox, "wave_frequency", v));
        SliderRow(vbox, "Amplitude",    0f,   0.6f, 0.13f, 0.005f,
            v => SyncWave(vbox, "wave_amplitude", v));
        SliderRow(vbox, "Speed",        0f,   20f,  5.0f, 0.1f,
            v => SyncWave(vbox, "wave_speed", v));
        SliderRow(vbox, "Head Flex",    0f,    0.3f, 0.02f, 0.005f,
            v => SyncWave(vbox, "head_flex", v));
        SliderRow(vbox, "Tail Flex",    0.5f,  2.5f, 1.05f, 0.01f,
            v => SyncWave(vbox, "tail_flex", v));
        Sep(vbox);

        // ── Toon Shading ─────────────────────────────────────────────────────
        SectionHeader(vbox, "Toon Shading");
        InfoLabel(vbox, "band1 < band2 < band3  (NdotL thresholds)");
        SliderRow(vbox, "band1 (deep shadow)", 0f, 0.4f,  0.10f, 0.01f,
            v => _fr.SetShaderParameter("band1", v));
        SliderRow(vbox, "band2 (shadow)",      0f, 0.8f,  0.35f, 0.01f,
            v => _fr.SetShaderParameter("band2", v));
        SliderRow(vbox, "band3 (highlight)",   0f, 1.0f,  0.68f, 0.01f,
            v => _fr.SetShaderParameter("band3", v));
        SliderRow(vbox, "Band Softness",       0.001f, 0.1f, 0.025f, 0.002f,
            v => _fr.SetShaderParameter("band_softness", v));
        SliderRow(vbox, "Specular Threshold",  0.80f, 1.0f, 0.91f, 0.005f,
            v => _fr.SetShaderParameter("specular_threshold", v));
        SliderRow(vbox, "Specular Hardness",   0.001f, 0.05f, 0.02f, 0.001f,
            v => _fr.SetShaderParameter("specular_hardness", v));
        SliderRow(vbox, "Rim Power",      1f,   8f,   3.5f, 0.1f,
            v => _fr.SetShaderParameter("rim_power", v));
        SliderRow(vbox, "Rim Strength",   0f,   4f,   1.6f, 0.05f,
            v => _fr.SetShaderParameter("rim_strength", v));
        SliderRow(vbox, "Rim Threshold",  0f,   1f,   0.45f, 0.01f,
            v => _fr.SetShaderParameter("rim_threshold", v));
        SliderRow(vbox, "Saturation",     0f,   3f,   1.5f, 0.05f,
            v => _fr.SetShaderParameter("saturation", v));
        Sep(vbox);

        // ── Colour palette ───────────────────────────────────────────────────
        SectionHeader(vbox, "Colour Palette");
        ColorRow(vbox, "Deep Shadow",  new Color(0.02f, 0.04f, 0.18f),
            c => _fr.SetShaderParameter("color_deep_shadow", c));
        ColorRow(vbox, "Shadow",       new Color(0.04f, 0.14f, 0.42f),
            c => _fr.SetShaderParameter("color_shadow", c));
        ColorRow(vbox, "Mid",          new Color(0.06f, 0.44f, 0.88f),
            c => _fr.SetShaderParameter("color_mid", c));
        ColorRow(vbox, "Highlight",    new Color(0.22f, 0.78f, 1.00f),
            c => _fr.SetShaderParameter("color_highlight", c));
        ColorRow(vbox, "Specular",     new Color(0.95f, 1.00f, 1.00f),
            c => _fr.SetShaderParameter("color_specular", c));
        ColorRow(vbox, "Rim",         new Color(0.40f, 0.90f, 1.00f),
            c => _fr.SetShaderParameter("color_rim", c));
        Sep(vbox);

        // ── Visual extras ────────────────────────────────────────────────────
        SectionHeader(vbox, "Visual");
        SliderRow(vbox, "Edge Glow",      0f, 2f, 0.4f, 0.05f,
            v => _fr.SetShaderParameter("edge_glow_strength", v));
        ColorRow(vbox, "Edge Glow Color", new Color(0.5f, 0.95f, 1.0f),
            c => _fr.SetShaderParameter("edge_glow_color", c));
        SliderRow(vbox, "Instance Tint",  0f, 1f, 0.35f, 0.01f,
            v => _fr.SetShaderParameter("instance_tint_strength", v));
        SliderRow(vbox, "Outline Width",  0f, 0.08f, 0.022f, 0.001f,
            v => _fr.SetShaderParameter("outline_width", v));
        ColorRow(vbox, "Outline Color",   new Color(0.01f, 0.04f, 0.14f),
            c => _fr.SetShaderParameter("outline_color", c));

        // Bottom padding
        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });
    }

    // Sync a param to both toon and outline shaders (wave params are in both).
    private void SyncWave(VBoxContainer _, string param, float value) =>
        _fr.SetShaderParameter(param, value);

    // ─────────────────────────────────────────────────────────────────────────
    // UI builder helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static LabelSettings MakeLabelSettings(float size, Color color) =>
        new() { FontSize = (int)size, FontColor = color };

    private void Heading(VBoxContainer parent, string text, Color color)
    {
        var lbl = new Label
        {
            Text              = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            LabelSettings     = MakeLabelSettings(17, color),
        };
        parent.AddChild(lbl);
    }

    private Label SmallLabel(VBoxContainer parent, string text,
                              Color? color = null)
    {
        var lbl = new Label
        {
            Text          = text,
            LabelSettings = MakeLabelSettings(11, color ?? Colors.LightGray),
            AutowrapMode  = TextServer.AutowrapMode.Word,
        };
        parent.AddChild(lbl);
        return lbl;
    }

    private void InfoLabel(VBoxContainer parent, string text)
    {
        var lbl = new Label
        {
            Text          = text,
            LabelSettings = MakeLabelSettings(10, new Color(0.65f, 0.65f, 0.65f)),
            AutowrapMode  = TextServer.AutowrapMode.Word,
        };
        parent.AddChild(lbl);
    }

    private void SectionHeader(VBoxContainer parent, string text)
    {
        var lbl = new Label
        {
            Text          = text,
            LabelSettings = MakeLabelSettings(12, new Color(1f, 0.82f, 0.25f)),
        };
        parent.AddChild(lbl);
    }

    private void Sep(VBoxContainer parent) => parent.AddChild(new HSeparator());

    // Slider row: label (130px) | HSlider | value label (52px)
    private void SliderRow(VBoxContainer parent, string label,
                            float min, float max, float initial, float step,
                            System.Action<float> onChange)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);

        var lbl = new Label
        {
            Text               = label,
            CustomMinimumSize  = new Vector2(132, 0),
            LabelSettings      = MakeLabelSettings(11, Colors.White),
            VerticalAlignment  = VerticalAlignment.Center,
            AutowrapMode       = TextServer.AutowrapMode.Word,
        };
        row.AddChild(lbl);

        var slider = new HSlider
        {
            MinValue              = min,
            MaxValue              = max,
            Value                 = initial,
            Step                  = step,
            SizeFlagsHorizontal   = Control.SizeFlags.ExpandFill,
            CustomMinimumSize     = new Vector2(0, 24),
        };
        row.AddChild(slider);

        var valLbl = new Label
        {
            Text              = initial.ToString("F3"),
            CustomMinimumSize = new Vector2(54, 0),
            LabelSettings     = MakeLabelSettings(11, new Color(0.6f, 0.9f, 1.0f)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.AddChild(valLbl);

        slider.ValueChanged += v =>
        {
            valLbl.Text = ((float)v).ToString("F3");
            onChange((float)v);
        };
    }

    // Integer spinner row (for fish count etc.)
    private SpinBox IntRow(VBoxContainer parent, string label,
                            int min, int max, int initial, int step,
                            System.Action<int> onChange)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);

        row.AddChild(new Label
        {
            Text              = label,
            CustomMinimumSize = new Vector2(132, 0),
            LabelSettings     = MakeLabelSettings(11, Colors.White),
        });

        var spin = new SpinBox
        {
            MinValue            = min,
            MaxValue            = max,
            Value               = initial,
            Step                = step,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        row.AddChild(spin);

        spin.ValueChanged += v => onChange((int)v);
        return spin;
    }

    // Colour picker row
    private void ColorRow(VBoxContainer parent, string label,
                           Color initial, System.Action<Color> onChange)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);

        row.AddChild(new Label
        {
            Text              = label,
            CustomMinimumSize = new Vector2(132, 0),
            LabelSettings     = MakeLabelSettings(11, Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var picker = new ColorPickerButton
        {
            Color               = initial,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize   = new Vector2(0, 28),
        };
        row.AddChild(picker);

        picker.ColorChanged += c => onChange(c);
    }

    // Button with accent colour
    private void Button(VBoxContainer parent, string text,
                         Color tint, System.Action onPress)
    {
        var btn = new Button
        {
            Text                = text,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        btn.AddThemeColorOverride("font_color", tint);
        btn.Pressed += onPress;
        parent.AddChild(btn);
    }
}
