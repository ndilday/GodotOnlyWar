using Godot;
using System;

public partial class DialogController : Control
{
    public const string DialogInputBlockerGroup = "dialog_input_blocker";

    public event EventHandler CloseButtonPressed;

    public override void _Ready()
    {
        AddToGroup(DialogInputBlockerGroup);

        foreach(Node child in GetChildren())
        {
            if(child is DialogView)
            {
                DialogView view = (DialogView)child;
                view.CloseButtonPressed += (object sender, EventArgs e) => CloseButtonPressed?.Invoke(this, e);
                break;
            }
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        // Scroll containers stop consuming wheel events when they reach an edge. Keep those
        // events (and any other input not used by the dialog) from falling through to the map.
        if (IsVisibleInTree())
        {
            GetViewport().SetInputAsHandled();
        }
    }
}
