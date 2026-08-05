using Godot;
using OnlyWar.Helpers.UI;
using System;

public partial class BottomMenu : Control
{
    public enum Destination
    {
        None,
        Chapter,
        Apothecarium,
        TrainingUnit,
        Fleet,
        Diplomacy
    }

    private readonly System.Collections.Generic.Dictionary<Destination, Button> _destinationButtons = [];

    public event EventHandler ChapterButtonPressed;
    public event EventHandler ApothecariumButtonPressed;
    public event EventHandler TrainingUnitButtonPressed;
    public event EventHandler FleetButtonPressed;
    public event EventHandler DiplomacyButtonPressed;
    public event EventHandler ArchiveButtonPressed;
    public event EventHandler EndTurnButtonPressed;

    public override void _Ready()
    {
        Button chapterButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/ChapterButton");
        RegisterDestination(Destination.Chapter, chapterButton);
        IconAtlas.Apply(chapterButton, "chapter", 92);
        chapterButton.Pressed += () => ChapterButtonPressed?.Invoke(this, EventArgs.Empty);
        Button apothecariumButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/ApothecariumButton");
        RegisterDestination(Destination.Apothecarium, apothecariumButton);
        IconAtlas.Apply(apothecariumButton, "apothecarium", 96);
        apothecariumButton.Pressed += () => ApothecariumButtonPressed?.Invoke(this, EventArgs.Empty);
        Button reclusiumButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/ReclusiumButton");
        IconAtlas.Apply(reclusiumButton, "reclusium", 92);
        Button libraryButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/LibraryButton");
        IconAtlas.Apply(libraryButton, "librarium", 92);
        Button armoryButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/ArmoryButton");
        IconAtlas.Apply(armoryButton, "armamentarium", 94);
        Button trainingUnitButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/TrainingUnitButton");
        RegisterDestination(Destination.TrainingUnit, trainingUnitButton);
        IconAtlas.Apply(trainingUnitButton, "training_unit", 96);
        trainingUnitButton.Pressed += () => TrainingUnitButtonPressed?.Invoke(this, EventArgs.Empty);
        Button fleetButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/FleetButton");
        RegisterDestination(Destination.Fleet, fleetButton);
        IconAtlas.Apply(fleetButton, "fleet", 92);
        fleetButton.Pressed += () => FleetButtonPressed?.Invoke(this, EventArgs.Empty);
        Button diplomacyButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/DiplomacyButton");
        RegisterDestination(Destination.Diplomacy, diplomacyButton);
        IconAtlas.Apply(diplomacyButton, "diplomacy", 96);
        diplomacyButton.Pressed += () => DiplomacyButtonPressed?.Invoke(this, EventArgs.Empty);
        Button archiveButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/ArchiveButton");
        IconAtlas.Apply(archiveButton, "archive", 92);
        archiveButton.Pressed += () => ArchiveButtonPressed?.Invoke(this, EventArgs.Empty);
        Button endTurnButton = GetNode<Button>("Panel/MarginContainer/HBoxContainer/EndTurnButton");
        IconAtlas.Apply(endTurnButton, "end_turn", 110);
        endTurnButton.Pressed += () => EndTurnButtonPressed?.Invoke(this, EventArgs.Empty);
    }

    public void SetActiveDestination(Destination destination)
    {
        foreach ((Destination key, Button button) in _destinationButtons)
        {
            button.SetPressedNoSignal(key == destination);
        }
    }

    private void RegisterDestination(Destination destination, Button button)
    {
        button.ToggleMode = true;
        _destinationButtons[destination] = button;
    }
}
