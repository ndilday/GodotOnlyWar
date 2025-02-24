using System.Drawing;

namespace OnlyWar.Helpers.Extensions
{
    public static class ColorExtensions
    {
        public static Godot.Color ToGodotColor(this Color systemColor)
        {
            return new Godot.Color(
                systemColor.R / 255f,
                systemColor.G / 255f,
                systemColor.B / 255f,
                systemColor.A / 255f
            );
        }
    }
}
