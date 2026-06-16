using Godot;
using System;

namespace OnlyWar.Scenes.MainGameScreen
{
    public partial class ClickableSprite2D: Sprite2D
    {
        public event EventHandler Pressed;
        public event EventHandler DoublePressed;

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventMouseButton emb)
            {
                if (emb.ButtonIndex == MouseButton.Left && emb.IsPressed() && IsPixelOpaque(GetLocalMousePosition()))
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
            }
        }
    }
}
