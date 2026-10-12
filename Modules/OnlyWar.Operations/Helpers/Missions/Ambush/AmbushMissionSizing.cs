using OnlyWar.Domain.Extensions;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Operations.Missions.Ambush
{
    /// <summary>
    /// Converts the intelligence-discovered ambush target into both its tactical force-generation
    /// budget and the player-facing number of squads needed to reach parity.
    /// </summary>
    public static class AmbushMissionSizing
    {
        /// <summary>
        /// Battle value of a squad template fielded at full strength, taken from the rules data
        /// (<see cref="SquadTemplate.BattleValue"/>: every element at the strength generation
        /// builds it to).
        /// </summary>
        public static long FullStrengthBattleValue(SquadTemplate template) =>
            template?.BattleValue ?? 0L;

        // The ambushed force is a slice of the enemy region faction's deployed strength, never more
        // than all of it. The share is log-uniform across the band, so a picket of 1% and a main body
        // of 100% are each one end of the same scale.
        //
        // This replaced a size band read as "10^size PDF-trooper equivalents" and converted to battle
        // value at 5 BV each. The cap on that band was taken from the enemy's battle value as if it
        // were a headcount, so the conversion counted the enemy twice: a 3,000 BV region could
        // produce a 50,000 BV ambush target (OnlyWar_TDD.md §6.2, "Ambush sizing").
        public const double MinimumTargetShare = 0.01;
        public const double MaximumTargetShare = 1.0;

        // The intelligence roll that found the opportunity moves the share up the band: a bare
        // success sees only a small detachment, a clear one finds the enemy's main body moving
        // through a killing ground. At this margin and above, the lowest roll starts at the top
        // third of the band. The ordinary ambush band is a z of 0 to 2 (PlanetIntelligenceProcessor).
        public const double FullIntelligenceMargin = 2.0;

        /// <summary>
        /// Rolls the battle value of the force an ambush catches: a share of
        /// <paramref name="deployedStrength"/> that rises with <paramref name="intelligenceMargin"/>.
        /// Zero when there is nothing deployed to ambush.
        /// </summary>
        public static long RollTargetBattleValue(
            long deployedStrength,
            double intelligenceMargin,
            IRNG random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            return TargetBattleValue(deployedStrength, intelligenceMargin, random.GetLinearDouble());
        }

        /// <summary>
        /// The target an ambush with no stored budget is expected to meet: the middle of the roll
        /// for a bare intelligence success. Used only where no roll has been made.
        /// </summary>
        public static long EstimateTargetBattleValue(long deployedStrength) =>
            TargetBattleValue(deployedStrength, intelligenceMargin: 0.0, roll: 0.5);

        internal static long TargetBattleValue(
            long deployedStrength,
            double intelligenceMargin,
            double roll)
        {
            if (deployedStrength <= 0) return 0;
            double margin = double.IsFinite(intelligenceMargin)
                ? Math.Clamp(intelligenceMargin, 0.0, FullIntelligenceMargin)
                : 0.0;
            double position = (margin + Math.Clamp(roll, 0.0, 1.0)) / (FullIntelligenceMargin + 1.0);
            double share = MinimumTargetShare
                * Math.Pow(MaximumTargetShare / MinimumTargetShare, position);
            double target = deployedStrength * share;
            return target >= deployedStrength
                ? deployedStrength
                : Math.Max(1L, (long)Math.Round(target));
        }

        /// <summary>
        /// The order-of-magnitude band shown for an ambush: floor(log10(target)), never below 1.
        /// Descriptive only; the stored target battle value is what sizes the fight.
        /// </summary>
        public static int MissionSizeBand(long targetBattleValue) =>
            targetBattleValue < 10
                ? 1
                : Math.Max(1, (int)Math.Floor(Math.Log10(targetBattleValue)));

        /// <summary>
        /// The budget the ambushed force is generated to. Intelligence-discovered ambushes store it
        /// when they are found; a mission without one (a dormant-population culling) rolls it now
        /// against the target's current deployed strength.
        /// </summary>
        public static long ResolveTargetBattleValue(Mission mission, IRNG random)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            return mission.TargetBattleValue
                ?? RollTargetBattleValue(
                    mission.RegionFaction?.GetDeployedStrength() ?? 0L,
                    intelligenceMargin: 0.0,
                    random);
        }

        /// <summary>
        /// How many full-strength reference squads reach parity with the target. A general measure
        /// for the player, deliberately not keyed to which squads happen to be in the region.
        /// </summary>
        public static long RecommendSquads(long targetBattleValue, long referenceSquadBattleValue)
        {
            if (referenceSquadBattleValue <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(referenceSquadBattleValue),
                    "A positive reference squad battle value is required.");
            }

            long positiveTarget = Math.Max(1L, targetBattleValue);
            long wholeSquads = positiveTarget / referenceSquadBattleValue;
            return Math.Max(
                1L,
                wholeSquads + (positiveTarget % referenceSquadBattleValue == 0 ? 0 : 1));
        }
    }
}
