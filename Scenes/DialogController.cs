using Godot;
using System;

public partial class DialogController : Control
{
    public const string DialogInputBlockerGroup = "dialog_input_blocker";

    public event EventHandler CloseButtonPressed;

    /// <summary>
    /// Requests the same close operation as the visible close button. Global gameplay input uses
    /// this for the X shortcut so each dialog keeps its existing owner-specific unwind behavior.
    /// </summary>
    public void RequestClose()
    {
        CloseButtonPressed?.Invoke(this, EventArgs.Empty);
    }

    public override void _Ready()
    {
        AddToGroup(DialogInputBlockerGroup);

        foreach(Node child in GetChildren())
        {
            if(child is DialogView)
            {
                DialogView view = (DialogView)child;
                view.CloseButtonPressed += (object sender, EventArgs e) => RequestClose();
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
