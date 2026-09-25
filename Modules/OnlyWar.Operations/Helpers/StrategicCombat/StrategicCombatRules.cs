using System;
using OnlyWar.Domain;
using OnlyWar.Domain.Orders;

namespace OnlyWar.Operations.StrategicCombat
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
        public const long TacticalMarineBattleValue = 9;

        // These strategic thresholds retain the pre-recalculation unit scale. BattleValues are
        // deliberately compressed to that same scale so garrison growth, force pools, and
        // tactical/strategic handoff decisions remain comparable.
        // Raised from 1500 on 2026-09-18. This is the handoff: at or above it a fight resolves
        // strategically, and strategic combat CANNOT finish a defender, because defenderLossRate is
        // clamped at 0.75 while HideBrokenCivilianDefender needs exactly zero. So every point of this
        // floor is mop-up that the game can actually conclude.
        //
        // Sized in 2026-09 against a 2:1 commitment, where `committed + defender` is about three times
        // the defender: 1500 put the crossover at a defender of ~500 and 3000 puts it at ~1000 -
        // roughly ten attacking squads against fewer than ten defending ones, which is well inside
        // what the tactical resolver handles. The allocator's minimum is now the 1.5:1 carry point
        // (ForceTaskBuilder), which moves the crossover up to a defender of ~1200 at the least
        // commitment and down as the auction adds force above it. Measured on Monody Prime, the
        // smallest strategic battle in a five-week run was 1753 against a defender of 553; under this
        // floor it resolves tactically and can end.
        public const long MassCombatBattleValueFloor = 3000;

        // Force ratio at which an attack stops being a battle and becomes an overrun: the defence is
        // swept aside and takes total casualties rather than the usual clamped share. The defender is
        // measured at `battleValue * EntrenchmentMultiplier(level)` - the same entrenchment curve the
        // fight itself uses, so works are exactly as protective here as they are anywhere else.
        //
        // This exists because the 0.75 loss clamp made annihilation unreachable at ANY ratio. Monody
        // Prime, 2026-09-18: 22,000 battle value committed against a defender of 18, and 45,153 Orks
        // sharing a region with 58 Imperials that they could not finish. Attacker casualties are
        // unchanged - an overrun is cheap, not free.
        public const double OverrunForceRatio = 10.0;

        // One point of reorganization effort reforms this much disorganized military BV. Anchored
        // to a ten-trooper PDF squad so the rate is expressed in the same currency as force pools.
        public const long ReorganizationBattleValuePerEffort = 10 * PdfTrooperBattleValue;

        // Each undefended day of an assault destroys this multiple of the surviving assaulting
        // force's BV. An unopposed assault is a rampage: the capacity below is spent on the
        // defender's broken formations first, then on troops that exist but never mustered, then on
        // the people living there.
        public const double UndefendedAssaultDestructionMultiplier = 1.0;

        // Troops who never formed up are still armed and in cover, so they cost twice what a broken
        // formation does: one point of capacity destroys this much organized BV.
        public const double OrganizedResistanceFactor = 0.5;

        // Capacity the fighting did not absorb falls on the population. This is not massacre as
        // policy - it is the chaos of an overrun - so every attacker inflicts it. Anchored on the
        // Imperium, where a PDF trooper is 5 BV for one man, so a point of capacity is about a
        // person. A faction whose numbers ARE its army has no civilians and so loses none.
        public const double CiviliansPerBattleValue = 1.0;

        // Ceiling on how much of a region's remaining civilian population one day of rampage can
        // kill. Without it a large enough horde empties a hive world in a single week.
        public const double MaximumDailyCivilianLossFraction = 0.05;

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
        // (FactionThreatAssessment.CalculateRequiredDefensiveBattleValue) even with no listening posts
        // or recon.
        public const float IntelGainedFromBeingAttacked = 2.0f;

        public static double AmbushSurpriseMultiplier(double attackerIntel, double defenderIntel) =>
            1.0 + Math.Min(MaxAmbushSurprise,
                           Math.Max(0.0, attackerIntel - defenderIntel) * AmbushSurprisePerIntel);

        public static double DefenderProtection(double entrenchment) =>
            Math.Max(1.0 / (1.0 + Math.Max(0.0, entrenchment) * 0.08), 0.35);
    }
}
