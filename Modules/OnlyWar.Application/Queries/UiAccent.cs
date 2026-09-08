namespace OnlyWar.Application;

/// <summary>
/// The meaning a projection attaches to a value, not the colour that renders it. Queries classify;
/// the Godot host owns the single accent-to-colour mapping in <c>OnlyWarStyle</c>. Adding a colour
/// here would put the palette back inside the application.
/// </summary>
public enum UiAccent
{
    Body,
    Muted,
    Gold,
    Player,
    Opposing,
    Contested,
    Stable,
    Warning,
    Critical
}
