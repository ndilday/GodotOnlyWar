using Godot;
using System;
using System.Collections.Generic;

public partial class EndOfTurnDialogView : DialogView
{
    private VBoxContainer _vboxContainer;

    public override void _Ready()
    {
        base._Ready();
        _vboxContainer = GetNode<VBoxContainer>("Panel/ScrollContainer/VBoxContainer");
    }

    public void AddData(IEnumerable<string> data)
    {
        foreach (string s in data)
        {
            RichTextLabel label = new RichTextLabel();
            label.Text = s;
            label.AnchorLeft = 0;
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _vboxContainer.AddChild(label);
        }
    }

    public void ClearData()
    {
        var existingLines = _vboxContainer.GetChildren();
        if (existingLines != null)
        {
            foreach (var line in existingLines)
            {
                _vboxContainer.RemoveChild(line);
                line.QueueFree();
            }
        }
    }
}
