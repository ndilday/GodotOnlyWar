using Godot;
using OnlyWar.Application;

namespace OnlyWar.Host.Presentation.UI
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

        public static Color For(MedicalWoundLevel level, bool severed = false, bool healthyCybernetic = false)
        {
            if (severed) return Lost;
            if (healthyCybernetic && level == MedicalWoundLevel.None) return HealthyCybernetic;
            return level switch
            {
                MedicalWoundLevel.Negligible => Negligible,
                MedicalWoundLevel.Minor => Minor,
                MedicalWoundLevel.Moderate => Moderate,
                MedicalWoundLevel.Major => Major,
                MedicalWoundLevel.Critical => Critical,
                MedicalWoundLevel.Massive => Massive,
                MedicalWoundLevel.Mortal => Mortal,
                MedicalWoundLevel.Unsurvivable => Unsurvivable,
                _ => Healthy
            };
        }

        private static Color FromRgb(byte red, byte green, byte blue) =>
            new(red / 255f, green / 255f, blue / 255f);
    }
}
