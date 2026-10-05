using OnlyWar.Operations.StrategicCombat;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Operations.Missions.Ambush
{
    /// <summary>
    /// The player's side of the "Recommended force" question: the current battle value of each
    /// squad that could be committed to the mission, plus the battle value of a full-strength
    /// reference squad (read from the rules data) for when none could be.
    /// </summary>
    public sealed record AmbushForceAvailability(
        IReadOnlyList<long> AvailableSquadBattleValues,
        long ReferenceSquadBattleValue);

    /// <summary>
    /// How many squads reach parity with a target. <see cref="ExceedsAvailable"/> is true when even
    /// every available squad together falls short; <see cref="Squads"/> is then the number
    /// available, not a number of squads the player does not have. <see cref="BasedOnAvailableSquads"/>
    /// is false when the count was taken against the reference squad instead.
    /// </summary>
    public readonly record struct SquadRecommendation(
        long Squads,
        bool ExceedsAvailable,
        bool BasedOnAvailableSquads);

    /// <summary>
    /// Converts the intelligence-discovered ambush target into both its tactical force-generation
    /// budget and the player-facing number of squads needed to reach parity.
    /// </summary>
    public static class AmbushMissionSizing
    {
        /// <summary>
        /// A squad's current value for committing it: the sum of its combat-effective members'
        /// template battle values, the same valuation a mission applies to the force it commits
        /// (<c>OperationalMissionElement.AbleBattleValue</c>).
        /// </summary>
        public static long SquadBattleValue(Squad squad) =>
            squad?.Members
                .Where(member => member != null && member.IsCombatEffective)
                .Sum(member => (long)(member.Template?.BattleValue ?? 0))
            ?? 0L;

        /// <summary>
        /// Battle value of a squad template fielded at full strength, taken from the rules data
        /// (<see cref="SquadTemplate.BattleValue"/>: every element at the strength generation
        /// builds it to).
        /// </summary>
        public static long FullStrengthBattleValue(SquadTemplate template) =>
            template?.BattleValue ?? 0L;

        public static long RollTargetBattleValue(int missionSize, IRNG random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));

            double pdfTrooperEquivalent = Math.Pow(
                10,
                Math.Max(1, missionSize) + random.GetLinearDouble());
            long forceSize = pdfTrooperEquivalent >= long.MaxValue
                ? long.MaxValue
                : Math.Max(1L, (long)pdfTrooperEquivalent);

            return SaturatingMultiply(forceSize, StrategicCombatRules.PdfTrooperBattleValue);
        }

        // Older saves have only the order-of-magnitude mission band. Use the geometric midpoint
        // of that band as a stable migration value so those opportunities also gain a recommendation
        // and no longer reroll their target after being assigned.
        public static long EstimateLegacyTargetBattleValue(int missionSize)
        {
            double pdfTrooperEquivalent = Math.Pow(10, Math.Max(1, missionSize) + 0.5);
            long forceSize = pdfTrooperEquivalent >= long.MaxValue
                ? long.MaxValue
                : Math.Max(1L, (long)pdfTrooperEquivalent);

            return SaturatingMultiply(forceSize, StrategicCombatRules.PdfTrooperBattleValue);
        }

        public static long ResolveTargetBattleValue(Mission mission, IRNG random)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            return mission.TargetBattleValue
                ?? RollTargetBattleValue(mission.MissionSize, random);
        }

        /// <summary>
        /// How many of the player's squads must be committed to reach parity with the target.
        /// Available squads are taken strongest-first until their combined battle value reaches the
        /// target. If all of them together still fall short the result says so rather than naming a
        /// number of squads the player does not have. With no available squad there is nothing to
        /// count, so the target is measured in reference squads instead.
        /// </summary>
        public static SquadRecommendation RecommendSquads(
            long targetBattleValue,
            AmbushForceAvailability availability)
        {
            if (availability == null) throw new ArgumentNullException(nameof(availability));

            long positiveTarget = Math.Max(1L, targetBattleValue);
            List<long> strongestFirst = (availability.AvailableSquadBattleValues ?? [])
                .Where(value => value > 0)
                .OrderByDescending(value => value)
                .ToList();

            if (strongestFirst.Count == 0)
            {
                long reference = availability.ReferenceSquadBattleValue;
                if (reference <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(availability),
                        "A positive reference squad battle value is required when no squad is available.");
                }
                long wholeSquads = positiveTarget / reference;
                return new SquadRecommendation(
                    Math.Max(1L, wholeSquads + (positiveTarget % reference == 0 ? 0 : 1)),
                    ExceedsAvailable: false,
                    BasedOnAvailableSquads: false);
            }

            long committed = 0;
            for (int index = 0; index < strongestFirst.Count; index++)
            {
                committed = SaturatingAdd(committed, strongestFirst[index]);
                if (committed >= positiveTarget)
                {
                    return new SquadRecommendation(
                        index + 1, ExceedsAvailable: false, BasedOnAvailableSquads: true);
                }
            }

            return new SquadRecommendation(
                strongestFirst.Count, ExceedsAvailable: true, BasedOnAvailableSquads: true);
        }

        private static long SaturatingAdd(long left, long right) =>
            left > long.MaxValue - right ? long.MaxValue : left + right;

        private static long SaturatingMultiply(long left, long right)
        {
            if (left <= 0 || right <= 0) return 0;
            return left > long.MaxValue / right ? long.MaxValue : left * right;
        }
    }
}
