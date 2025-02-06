using Godot;
using System;
using System.Collections.Generic;

public partial class ApothecariumScreenView : DialogView
{
    private RichTextLabel _geneseedReportRichText;
    private RichTextLabel _injuryDetailRichText;
    private Button _closeButton;
    private VBoxContainer _squadVBox;
    private ButtonGroup _squadButtonGroup;

    public event EventHandler<int> SquadButtonPressed;
    public event EventHandler CloseButtonPressed;

    public override void _Ready()
    {
        base._Ready();
        _geneseedReportRichText = GetNode<RichTextLabel>("GeneseedPanel/RichTextLabel");
        _injuryDetailRichText = GetNode<RichTextLabel>("InjuryReportPanel/RichTextLabel");
        _squadVBox = GetNode<VBoxContainer>("InjuryReportPanel/ScrollContainer/VBoxContainer");
        _squadButtonGroup = new ButtonGroup();
    }

    public void PopulateGeneseedReport(string geneseedReport)
    {
        _geneseedReportRichText.Text = geneseedReport;
    }

    public void PopulateSquadList(IReadOnlyList<Tuple<int, string>> squads)
    {
        ClearVBox(_squadVBox);
        foreach (Tuple<int, string> squad in squads)
        {
            AddSquad(squad.Item1, squad.Item2);
        }
    }

    public void PopulateInjuryDetail(string injuryDetail)
    {
        _injuryDetailRichText.Text = injuryDetail;
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
