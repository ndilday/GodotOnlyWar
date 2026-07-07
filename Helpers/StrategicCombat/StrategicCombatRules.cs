using System;
using OnlyWar.Models;
using OnlyWar.Models.Orders;

namespace OnlyWar.Helpers.StrategicCombat
{
    public static class StrategicCombatRules
    {
        public const long MassCombatBattleValueFloor = 1500;
        public const int MaxTacticalActors = 120;
        public const int MaxGeneratedSquads = 24;
        public const long NpcReconBattleValueCap = 200;

        public const double CombatSigma = 0.12;
        public const double BaseIntensity = 0.08;
        public const double CaptureThreshold = 1.10;

        public static double AggressionStrengthMultiplier(Aggression aggression) => aggression switch
        {
            Aggression.Avoid => 0.60,
            Aggression.Cautious => 0.80,
            Aggression.Normal => 1.00,
            Aggression.Attritional => 1.15,
            Aggression.Aggressive => 1.30,
            _ => 1.00
        };

        public static double AggressionCasualtyMultiplier(Aggression aggression) => aggression switch
        {
            Aggression.Avoid => 0.50,
            Aggression.Cautious => 0.75,
            Aggression.Normal => 1.00,
            Aggression.Attritional => 1.25,
            Aggression.Aggressive => 1.50,
            _ => 1.00
        };

        public static double FactionQuality(Faction faction)
        {
            if (faction == null) return 1.0;
            if (faction.IsDefaultFaction) return 1.0;
            if (faction.GrowthType == GrowthType.Consumption) return 1.15;
            if (faction.GrowthType == GrowthType.Conversion) return 0.85;
            return 1.0;
        }

        public static double DefenderReadiness(int organization) =>
            0.35 + 0.65 * Math.Clamp(organization, 0, 100) / 100.0;

        public static double EntrenchmentMultiplier(int entrenchment) =>
            Math.Min(3.0, 1.0 + Math.Max(0, entrenchment) * 0.10);

        public static double DetectionMultiplier(int detection) =>
            Math.Min(1.5, 1.0 + Math.Max(0, detection) * 0.02);

        public static double DefenderProtection(int entrenchment) =>
            Math.Max(1.0 / (1.0 + Math.Max(0, entrenchment) * 0.08), 0.35);
    }
}
