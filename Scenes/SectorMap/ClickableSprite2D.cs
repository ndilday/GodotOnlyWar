using Godot;
using System;

namespace OnlyWar.Scenes.MainGameScreen
{
    public partial class ClickableSprite2D: Sprite2D
    {
        public event EventHandler Pressed;

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventMouseButton emb)
            {
                if (emb.ButtonIndex == MouseButton.Left && emb.IsPressed() && IsPixelOpaque(GetLocalMousePosition()))
                {
                    Pressed.Invoke(this, EventArgs.Empty);
                    GetViewport().SetInputAsHandled();
                }
            }
        }
    }
}
