using Godot;
using System;

namespace OnlyWar.Scenes.MainGameScreen
{
    public partial class ClickableSprite2D: Sprite2D
    {
        public event EventHandler Pressed;
        public event EventHandler DoublePressed;
        public event EventHandler RightPressed;

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventMouseButton emb)
            {
                if (!emb.IsPressed() || !IsPixelOpaque(GetLocalMousePosition())) return;

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
