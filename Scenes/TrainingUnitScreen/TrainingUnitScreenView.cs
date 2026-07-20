using Godot;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;

public partial class TrainingUnitScreenView : Control
{
    private Button _closeButton;
    private OptionButton _focusOption;
    private RichTextLabel _squadReadinessRichText;
    private VBoxContainer _squadVBox;
    private ButtonGroup _squadButtonGroup;

    public event EventHandler CloseButtonPressed;
    public event EventHandler<int> SquadButtonPressed;
    public event EventHandler<TrainingFocuses> TrainingFocusSelected;
    public event EventHandler<Variant> LinkClicked;

    public override void _Ready()
    {
        _closeButton = GetNode<Button>("CloseButton");
        _closeButton.Pressed += () => CloseButtonPressed?.Invoke(this, EventArgs.Empty);
        _focusOption = GetNode<OptionButton>("SquadReportPanel/FocusHBox/FocusOption");
        PopulateFocusOptions();
        _focusOption.ItemSelected += OnFocusOptionSelected;
        _focusOption.Disabled = true;
        _squadReadinessRichText = GetNode<RichTextLabel>("SquadReportPanel/RichTextLabel");
        _squadReadinessRichText.MetaClicked += (Variant meta) => LinkClicked?.Invoke(this, meta); ;
        _squadVBox = GetNode<VBoxContainer>("SquadList/ScrollContainer/VBoxContainer");
        _squadButtonGroup = new ButtonGroup();
    }

    public void PopulateSquadList(IReadOnlyList<ValueTuple<int, string>> squads)
    {
        ClearVBox(_squadVBox);
        foreach (ValueTuple<int, string> squad in squads)
        {
            AddSquad(squad.Item1, squad.Item2);
        }
    }

    public void PopulateSquadReadinessReport(string text)
    {
        _squadReadinessRichText.Text = text;
    }

    public void SelectTrainingFocus(TrainingFocuses focus)
    {
        _focusOption.Disabled = false;
        _focusOption.Select(_focusOption.GetItemIndex((int)focus));
    }

    private void PopulateFocusOptions()
    {
        _focusOption.Clear();
        _focusOption.AddItem("Balanced", (int)TrainingFocuses.None);
        _focusOption.AddItem("Physical", (int)TrainingFocuses.Physical);
        _focusOption.AddItem("Vehicles", (int)TrainingFocuses.Vehicles);
        _focusOption.AddItem("Melee", (int)TrainingFocuses.Melee);
        _focusOption.AddItem("Ranged", (int)TrainingFocuses.Ranged);
    }

    private void OnFocusOptionSelected(long index)
    {
        TrainingFocusSelected?.Invoke(this, (TrainingFocuses)_focusOption.GetItemId((int)index));
    }

    private void ClearVBox(VBoxContainer vbox)
    {
        var existingButtons = vbox.GetChildren();
        if (existingButtons != null)
        {
            foreach (var child in existingButtons)
            {
                vbox.RemoveChild(child);
                child.QueueFree();
            }
        }
    }

    private void AddSquad(int id, string name)
    {
        Button squadButton = new Button();
        squadButton.Text = name;
        squadButton.SetMeta("id", id);
        squadButton.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        squadButton.Pressed += () => SquadButtonPressed?.Invoke(this, id);
        squadButton.ToggleMode = true;
        squadButton.ButtonGroup = _squadButtonGroup;
        _squadVBox.AddChild(squadButton);
    }
}
