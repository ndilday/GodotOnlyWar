using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class DialogController : Control
{
    public const string DialogInputBlockerGroup = "dialog_input_blocker";

    public event EventHandler CloseButtonPressed;
    private DialogView _dialogView;
    private bool _isTopDialog;

    /// <summary>
    /// Requests the same close operation as the visible close button. Global gameplay input uses
    /// this for the X shortcut so each dialog keeps its existing owner-specific unwind behavior.
    /// </summary>
    public void RequestClose()
    {
        if (_dialogView != null && !_isTopDialog)
        {
            return;
        }
        CloseButtonPressed?.Invoke(this, EventArgs.Empty);
    }

    public override void _Ready()
    {
        AddToGroup(DialogInputBlockerGroup);
        VisibilityChanged += OnDialogVisibilityChanged;

        foreach(Node child in GetChildren())
        {
            if(child is DialogView)
            {
                DialogView view = (DialogView)child;
                _dialogView = view;
                view.CloseButtonPressed += (object sender, EventArgs e) => RequestClose();
                break;
            }
        }

        Callable.From(RefreshDialogStack).CallDeferred();
    }

    public override void _ExitTree()
    {
        VisibilityChanged -= OnDialogVisibilityChanged;
        Callable.From(RefreshDialogStack).CallDeferred();
    }

    private void OnDialogVisibilityChanged()
    {
        Callable.From(RefreshDialogStack).CallDeferred();
    }

    private void RefreshDialogStack()
    {
        if (!IsInsideTree())
        {
            return;
        }

        List<DialogController> dialogs = GetTree()
            .GetNodesInGroup(DialogInputBlockerGroup)
            .OfType<DialogController>()
            .Where(dialog => dialog.IsInsideTree())
            .ToList();
        DialogController topDialog = FindTopmostVisibleDialog(dialogs);

        foreach (DialogController dialog in dialogs)
        {
            dialog._isTopDialog = dialog == topDialog;
            dialog._dialogView?.SetTopDialogState(dialog == topDialog);
        }
    }

    internal static DialogController FindTopmostVisibleDialog(
        IEnumerable<DialogController> dialogs)
    {
        return (dialogs ?? Enumerable.Empty<DialogController>())
            .Where(dialog => dialog?.IsInsideTree() == true && dialog.IsVisibleInTree())
            .Aggregate(
                (DialogController)null,
                (top, candidate) => top == null || candidate.IsGreaterThan(top)
                    ? candidate
                    : top);
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
