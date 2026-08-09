using Godot;

/// <summary>
/// Inspector-editable appearance and zoom range for one world-space sector-map label band.
/// Fonts are intentionally typed as FontVariation so tracking and OpenType settings remain
/// part of the resource rather than being hidden in renderer code.
/// </summary>
[GlobalClass]
public partial class SectorLabelBandStyle : Resource
{
    [Export]
    public FontVariation Font { get; set; }

    [Export(PropertyHint.Range, "0.5,128,0.5")]
    public float WorldFontSize { get; set; } = 10.0f;

    [Export(PropertyHint.Range, "0.0,10.0,0.25")]
    public float MinZoom { get; set; } = 0.0f;

    [Export(PropertyHint.Range, "0.0,20.0,0.25")]
    public float MaxZoom { get; set; } = 10.0f;

    [Export]
    public Color FontColor { get; set; } = Colors.White;

    [Export]
    public Color OutlineColor { get; set; } = new Color(0.0f, 0.0f, 0.0f, 0.82f);

    [Export(PropertyHint.Range, "0,8,1")]
    public int OutlineWidth { get; set; } = 2;

    [Export]
    public Color ShadowColor { get; set; } = new Color(0.0f, 0.0f, 0.0f, 0.70f);

    [Export(PropertyHint.Range, "0,8,1")]
    public int ShadowSize { get; set; } = 1;

    [Export]
    public Vector2 ShadowOffset { get; set; } = new Vector2(1.0f, 1.0f);

    [Export(PropertyHint.Range, "-4,12,0.25")]
    public float LetterSpacing { get; set; } = 1.0f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float Opacity { get; set; } = 1.0f;
}
