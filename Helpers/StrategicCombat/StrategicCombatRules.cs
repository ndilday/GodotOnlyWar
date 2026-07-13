using System;
using OnlyWar.Models;
using OnlyWar.Models.Orders;

namespace OnlyWar.Helpers.StrategicCombat
{
    public static class StrategicCombatRules
    {
        public const int MaxTacticalActors = 120;
        public const int MaxGeneratedSquads = 24;

        // Battle-value anchors from the shipped rules database (OnlyWar.s3db), regenerated
        // with the engine-faithful BattleValueCalculator model after the melee rework and
        // defense-skill fix: used to keep strategic thresholds tied to real soldiers rather
        // than the old implicit "10 BV per trooper" rule of thumb.
        public const long PdfTrooperBattleValue = 5;
        public const long HormagauntBattleValue = 7;
        public const long GenestealerBattleValue = 13;
        public const long TacticalMarineBattleValue = 9;
        public const long MeleeCarnifexBattleValue = 30;

        // These strategic thresholds retain the pre-recalculation unit scale. BattleValues are
        // deliberately compressed to that same scale so garrison growth, force pools, and
        // tactical/strategic handoff decisions remain comparable.
        public const long MassCombatBattleValueFloor = 1500;

        // A recon probe should stay within the existing small-detachment budget. Each faction's
        // own MinimumForceRequest can still floor this up to a full squad when its data requires it.
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

        public static double EntrenchmentMultiplier(double entrenchment) =>
            Math.Min(3.0, 1.0 + Math.Max(0.0, entrenchment) * 0.10);

        // Per point of intel advantage the attacker holds over the defender, and the cap on the
        // resulting surprise bonus. This replaces the old flat defender DetectionMultiplier: a
        // defender's awareness no longer makes it intrinsically stronger, it only denies the attacker
        // surprise. When the attacker understands the battlespace better than the defender sees its
        // own ground (a cult rising from within a blind PDF region), the attacker strikes with an
        // edge that fades as the defender builds awareness via listening posts, patrols, and recon.
        public const double AmbushSurprisePerIntel = 0.10;
        public const double MaxAmbushSurprise = 0.50;

        // Awareness a defender gains of each region an attack staged from, purely by being hit from
        // there: the blow itself reveals where the enemy is massing. This is the reactive path that
        // lets a previously-blind defender size its defensive need against a neighbour the following turn
        // (FactionStrategyController.CalculateRequiredGarrison) even with no listening posts or recon.
        public const float IntelGainedFromBeingAttacked = 2.0f;

        public static double AmbushSurpriseMultiplier(double attackerIntel, double defenderIntel) =>
            1.0 + Math.Min(MaxAmbushSurprise,
                           Math.Max(0.0, attackerIntel - defenderIntel) * AmbushSurprisePerIntel);

        public static double DefenderProtection(double entrenchment) =>
            Math.Max(1.0 / (1.0 + Math.Max(0.0, entrenchment) * 0.08), 0.35);
    }
}
