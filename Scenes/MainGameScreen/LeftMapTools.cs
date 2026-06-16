using Godot;
using OnlyWar.Helpers.UI;
using System;

public partial class LeftMapTools : Control
{
    public event EventHandler<string> MapToolPressed;

    public override void _Ready()
    {
        WireToolButton("Panel/MarginContainer/VBoxContainer/ToolButtons/FocusButton", "focus", "focus");
        WireToolButton("Panel/MarginContainer/VBoxContainer/ToolButtons/ZoomInButton", "zoom_in", "zoom_in");
        WireToolButton("Panel/MarginContainer/VBoxContainer/ToolButtons/ZoomOutButton", "zoom_out", "zoom_out");
        WireToolButton("Panel/MarginContainer/VBoxContainer/ToolButtons/LayersButton", "layers", "layers");
        WireToolButton("Panel/MarginContainer/VBoxContainer/ToolButtons/FilterButton", "filter", "filter");
    }

    private void WireToolButton(string path, string iconKey, string actionKey)
    {
        Button button = GetNode<Button>(path);
        IconAtlas.Apply(button, iconKey);
        button.Pressed += () => MapToolPressed?.Invoke(this, actionKey);
    }
}
