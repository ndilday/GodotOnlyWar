using Godot;
using System;
using System.Collections.Generic;

public partial class ConquistorumScreenView : Control
{
    private Button _closeButton;
    private RichTextLabel _squadReadinessRichText;
    private VBoxContainer _squadVBox;
    private ButtonGroup _squadButtonGroup;

    public event EventHandler CloseButtonPressed;
    public event EventHandler<int> SquadButtonPressed;
    public event EventHandler<Variant> LinkClicked;

    public override void _Ready()
    {
        _closeButton = GetNode<Button>("CloseButton");
        _closeButton.Pressed += () => CloseButtonPressed?.Invoke(this, EventArgs.Empty);
        _squadReadinessRichText = GetNode<RichTextLabel>("SquadReportPanel/RichTextLabel");
        _squadReadinessRichText.MetaClicked += (Variant meta) => LinkClicked?.Invoke(this, meta); ;
        _squadVBox = GetNode<VBoxContainer>("SquadList/ScrollContainer/VBoxContainer");
        _squadButtonGroup = new ButtonGroup();
    }

    public void PopulateSquadList(IReadOnlyList<Tuple<int, string>> squads)
    {
        ClearVBox(_squadVBox);
        foreach (Tuple<int, string> squad in squads)
        {
            AddSquad(squad.Item1, squad.Item2);
        }
    }

    public void PopulateSquadReadinessReport(string text)
    {
        _squadReadinessRichText.Text = text;
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
