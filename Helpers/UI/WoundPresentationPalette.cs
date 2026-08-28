using Godot;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.UI
{
    public static class WoundPresentationPalette
    {
        public static readonly Color Healthy = FromRgb(86, 163, 94);
        public static readonly Color Negligible = FromRgb(201, 190, 70);
        public static readonly Color Minor = FromRgb(219, 177, 55);
        public static readonly Color Moderate = FromRgb(221, 139, 45);
        public static readonly Color Major = FromRgb(218, 95, 40);
        public static readonly Color Critical = FromRgb(235, 71, 51);
        public static readonly Color Massive = FromRgb(204, 50, 42);
        public static readonly Color Mortal = FromRgb(166, 35, 32);
        public static readonly Color Unsurvivable = FromRgb(121, 24, 25);
        public static readonly Color Lost = FromRgb(142, 31, 31);
        public static readonly Color HealthyCybernetic = FromRgb(77, 179, 199);

        public static Color For(WoundLevel level, bool severed = false, bool healthyCybernetic = false)
        {
            if (severed) return Lost;
            if (healthyCybernetic && level == WoundLevel.None) return HealthyCybernetic;
            return level switch
            {
                WoundLevel.Negligible => Negligible,
                WoundLevel.Minor => Minor,
                WoundLevel.Moderate => Moderate,
                WoundLevel.Major => Major,
                WoundLevel.Critical => Critical,
                WoundLevel.Massive => Massive,
                WoundLevel.Mortal => Mortal,
                WoundLevel.Unsurvivable => Unsurvivable,
                _ => Healthy
            };
        }

        public static int SeverityTicks(WoundLevel level) => level switch
        {
            WoundLevel.Negligible => 1,
            WoundLevel.Minor => 2,
            WoundLevel.Moderate => 3,
            WoundLevel.Major => 4,
            WoundLevel.Critical or WoundLevel.Massive or WoundLevel.Mortal
                or WoundLevel.Unsurvivable => 5,
            _ => 0
        };

        private static Color FromRgb(byte red, byte green, byte blue) =>
            new(red / 255f, green / 255f, blue / 255f);
    }
}
