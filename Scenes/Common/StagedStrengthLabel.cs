using Godot;
using OnlyWar.Helpers.UI;

public partial class StagedStrengthLabel : HBoxContainer
{
    public void SetStrength(int current, int outgoing, int incoming, int maximum, string tooltip)
    {
        foreach (Node child in GetChildren()) child.QueueFree();
        AddPart(current.ToString(), OnlyWarStyle.BodyText);
        if (outgoing > 0) AddPart($" -{outgoing}", OnlyWarStyle.Critical);
        if (incoming > 0) AddPart($" +{incoming}", OnlyWarStyle.MedicalStable);
        AddPart($" / {maximum}", OnlyWarStyle.MutedText);
        TooltipText = tooltip;
        TooltipText = string.IsNullOrWhiteSpace(tooltip)
            ? $"{current} current, {outgoing} outgoing, {incoming} incoming, {maximum} maximum"
            : tooltip;
    }

    private void AddPart(string text, Color color)
    {
        Label label = new() { Text = text };
        label.AddThemeColorOverride("font_color", color);
        AddChild(label);
    }
}
