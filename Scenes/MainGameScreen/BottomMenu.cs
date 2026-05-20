using Godot;
using System;

public partial class BottomMenu : Control
{
    public event EventHandler ChapterButtonPressed;
    public event EventHandler ApothecariumButtonPressed;
    public event EventHandler TrainingUnitButtonPressed;
    public event EventHandler FleetButtonPressed;
    public event EventHandler EndTurnButtonPressed;

    public override void _Ready()
    {
        Button chapterButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/ChapterButton");
        chapterButton.Pressed += () => ChapterButtonPressed?.Invoke(this, EventArgs.Empty);
        Button apothecariumButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/ApothecariumButton");
        apothecariumButton.Pressed += () => ApothecariumButtonPressed?.Invoke(this, EventArgs.Empty);
        Button trainingUnitButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/TrainingUnitButton");
        trainingUnitButton.Pressed += () => TrainingUnitButtonPressed?.Invoke(this, EventArgs.Empty);
        Button fleetButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/FleetButton");
        fleetButton.Pressed += () => FleetButtonPressed?.Invoke(this, EventArgs.Empty);
        Button endTurnButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/EndTurnButton");
        endTurnButton.Pressed += () => EndTurnButtonPressed?.Invoke(this, EventArgs.Empty);
    }
}
