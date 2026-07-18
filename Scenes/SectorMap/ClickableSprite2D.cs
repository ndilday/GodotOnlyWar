using Godot;
using System;

namespace OnlyWar.Scenes.MainGameScreen
{
    public partial class ClickableSprite2D: Sprite2D
    {
        public event EventHandler Pressed;
        public event EventHandler DoublePressed;
        public event EventHandler RightPressed;

        // _UnhandledInput, not _Input: GUI controls (and the dialog input blocker)
        // must get first claim on clicks, or map sprites steal clicks aimed at
        // overlapping UI panels.
        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton emb)
            {
                if (!emb.IsPressed() || !IsVisibleInTree() || !IsPixelOpaque(GetLocalMousePosition())) return;

                if (emb.ButtonIndex == MouseButton.Left)
                {
                    if (emb.DoubleClick)
                    {
                        DoublePressed?.Invoke(this, EventArgs.Empty);
                    }
                    else
                    {
                        Pressed?.Invoke(this, EventArgs.Empty);
                    }
                    GetViewport().SetInputAsHandled();
                }
                else if (emb.ButtonIndex == MouseButton.Right)
                {
                    RightPressed?.Invoke(this, EventArgs.Empty);
                    GetViewport().SetInputAsHandled();
                }
            }
        }
    }
}
