using Godot;
using System;

public partial class OrderDialogView : Panel
{
    private RichTextLabel _headerLabel;
    private OptionButton _regionOption;
    private OptionButton _missionOption;
    private RichTextLabel _missionDescription;
    private OptionButton _aggressionOption;
    private RichTextLabel _aggressionDescription;

    public override void _Ready()
    {
        _headerLabel = GetNode<RichTextLabel>("Panel/HeaderLabel");
        _regionOption = GetNode<OptionButton>("VBoxContainer/RegionHBox/RegionOption");
        _missionOption = GetNode<OptionButton>("VBoxContainer/MissionHBox/MissionOption");
        _missionDescription = GetNode<RichTextLabel>("VBoxContainer/MissionDescriptionHBox/MissionDescription");
        _aggressionOption = GetNode<OptionButton>("VBoxContainer/AggressionHBox/AggressionOption");
        _aggressionDescription = GetNode<RichTextLabel>("VBoxContainer/AggressionDescriptionHBox/AggressionDescription");
    }

    public void SetHeader(string header)
    {
        _headerLabel.Text = header;
    }
}
