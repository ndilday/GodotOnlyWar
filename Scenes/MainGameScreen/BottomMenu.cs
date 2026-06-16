using Godot;
using OnlyWar.Helpers.UI;
using System;

public partial class BottomMenu : Control
{
    public event EventHandler ChapterButtonPressed;
    public event EventHandler ApothecariumButtonPressed;
    public event EventHandler TrainingUnitButtonPressed;
    public event EventHandler FleetButtonPressed;
    public event EventHandler DiplomacyButtonPressed;
    public event EventHandler EndTurnButtonPressed;

    public override void _Ready()
    {
        Button chapterButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/ChapterButton");
        IconAtlas.Apply(chapterButton, "chapter", 92);
        chapterButton.Pressed += () => ChapterButtonPressed?.Invoke(this, EventArgs.Empty);
        Button apothecariumButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/ApothecariumButton");
        IconAtlas.Apply(apothecariumButton, "apothecarium", 96);
        apothecariumButton.Pressed += () => ApothecariumButtonPressed?.Invoke(this, EventArgs.Empty);
        Button reclusiumButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/ReclusiumButton");
        IconAtlas.Apply(reclusiumButton, "reclusium", 92);
        Button libraryButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/LibraryButton");
        IconAtlas.Apply(libraryButton, "librarium", 92);
        Button armoryButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/ArmoryButton");
        IconAtlas.Apply(armoryButton, "armamentarium", 94);
        Button trainingUnitButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/TrainingUnitButton");
        IconAtlas.Apply(trainingUnitButton, "training_unit", 96);
        trainingUnitButton.Pressed += () => TrainingUnitButtonPressed?.Invoke(this, EventArgs.Empty);
        Button fleetButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/FleetButton");
        IconAtlas.Apply(fleetButton, "fleet", 92);
        fleetButton.Pressed += () => FleetButtonPressed?.Invoke(this, EventArgs.Empty);
        Button diplomacyButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/DiplomacyButton");
        IconAtlas.Apply(diplomacyButton, "diplomacy", 96);
        diplomacyButton.Pressed += () => DiplomacyButtonPressed?.Invoke(this, EventArgs.Empty);
        Button archiveButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/ArchiveButton");
        IconAtlas.Apply(archiveButton, "archive", 92);
        Button endTurnButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/EndTurnButton");
        IconAtlas.Apply(endTurnButton, "end_turn", 110);
        endTurnButton.Pressed += () => EndTurnButtonPressed?.Invoke(this, EventArgs.Empty);
    }
}
