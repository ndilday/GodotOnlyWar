using OnlyWar.Battles.Abstractions;
using OnlyWar.Domain.Extensions;
using OnlyWar.Domain.Missions;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Operations.Missions
{
    /// <summary>
    /// The distance at which a mission's engagement opens, slid between the two sides' preferred
    /// opening ranges by how well the mission force's controlling check went.
    /// </summary>
    /// <remarks>
    /// Interpolating between the two PREFERENCES — rather than scaling down from a fixed number —
    /// is what makes the margin mean "whose terms is this fought on" instead of "how close is it."
    /// Those are not the same question, and only the first one is answerable without knowing what
    /// the two forces are carrying. Marines ambushing Tyranids do not want to be as close as
    /// possible; they want bolter range, and the gribblies want contact. A formula that only ever
    /// pulls toward zero hands a well-executed marine ambush the Tyranids' preferred fight.
    ///
    /// Because it interpolates rather than extrapolates, neither side is ever dragged past the
    /// other's preference: the worst a mission force can do is fight at exactly the range its
    /// enemy wanted.
    ///
    /// PHASE 7 (Design/Reference/BattleLogic.md): each side's preference is now the
    /// derived engagement band against the OTHER SIDE'S ACTUAL FORCE, not the un-opposed
    /// saturation range of a curve fired at one randomly drawn representative. The interpolation
    /// itself is unchanged; what changed is that both endpoints are now the range each side would
    /// steer to anyway, so neither side opens a fight several hundred yards from where it intends
    /// to fight it.
    /// </remarks>
    public static class MissionOpeningRange
    {
        /// <param name="marginOfSuccess">
        /// The mission force's controlling check. Higher means the engagement opens nearer the
        /// mission force's own preference; at or below zero it opens at the enemy's.
        /// </param>
        public static ushort Interpolate(
            IReadOnlyList<OperationalMissionElement> missionSquads,
            IReadOnlyList<OperationalMissionElement> opposingSquads,
            IEngagementResolver resolver,
            float marginOfSuccess,
            IRNG random)
        {
            if (resolver == null) throw new System.ArgumentNullException(nameof(resolver));
            float rangeModifier = GaussianCalculator.ApproximateNormalCDF(marginOfSuccess);
            // RNG-STREAM ANCHOR, and nothing else. Phase 6 drew one representative member per side
            // to stand in for that side's target profile, opposing-side first, to preserve the RNG
            // order this logic had when it lived inline in MeetingEngagementMissionStep. Phase 7
            // derives each preference against the WHOLE opposing force, so the draws no longer feed
            // anything -- but dropping them would shift every seeded mission battle's RNG stream,
            // mixing a wholesale re-baseline into a change whose only intended effect is the range.
            // Keeping them makes any divergence below attributable to the opening range itself.
            _ = opposingSquads.First().GetRandomAbleMember(random);
            _ = missionSquads.First().GetRandomAbleMember(random);
            IReadOnlyList<EngagementParticipant> missionParticipants = missionSquads
                .Select(squad => squad.ToEngagementParticipant())
                .ToArray();
            IReadOnlyList<EngagementParticipant> opposingParticipants = opposingSquads
                .Select(squad => squad.ToEngagementParticipant())
                .ToArray();
            // Each squad plans its own part of the fight within its force, not the whole of it.
            double missionRange = missionSquads.Average(
                squad => resolver.GetPreferredOpeningRange(
                    squad.ToEngagementParticipant(), opposingParticipants, missionParticipants));
            double opposingRange = opposingSquads.Average(
                squad => resolver.GetPreferredOpeningRange(
                    squad.ToEngagementParticipant(), missionParticipants, opposingParticipants));
            return (ushort)(opposingRange + (missionRange - opposingRange) * rangeModifier);
        }

        /// <summary>
        /// The opening of a reciprocal assault: two forces meeting in the field, neither on its own
        /// terms. It opens at the LONGER of the two sides' preferences (rule B,
        /// Design/Reference/ForecastCalibration.md section 1, product owner 2026-10-22): forces meeting
        /// on open ground see each other from far off, the side that wants range opens fire as soon as
        /// it is worth firing, and the side that wants contact cannot make the fight start closer
        /// without the surprise that an ambush's margin represents. A side's preference is the
        /// average over its squads that have one; a squad with no preference (no ranged weapon)
        /// does not pull it. With no preference on either side the forces meet in contact.
        /// </summary>
        /// <remarks>
        /// Until 2026-10-24 an assault opened at the midpoint (Interpolate with margin 0), which gave
        /// a melee horde half of a gunline's chosen distance, and averaged a contact seeker's
        /// degenerate 0 into its side.
        /// </remarks>
        public static ushort ReciprocalAssault(
            IReadOnlyList<OperationalMissionElement> firstForce,
            IReadOnlyList<OperationalMissionElement> secondForce,
            IEngagementResolver resolver)
        {
            if (resolver == null) throw new System.ArgumentNullException(nameof(resolver));
            IReadOnlyList<EngagementParticipant> first = firstForce
                .Select(squad => squad.ToEngagementParticipant())
                .ToArray();
            IReadOnlyList<EngagementParticipant> second = secondForce
                .Select(squad => squad.ToEngagementParticipant())
                .ToArray();
            double? SidePreference(
                IReadOnlyList<EngagementParticipant> side,
                IReadOnlyList<EngagementParticipant> enemy)
            {
                List<int> preferences = side
                    .Select(squad => resolver.GetPreferredOpeningRangeOrNone(squad, enemy, side))
                    .Where(preference => preference.HasValue)
                    .Select(preference => preference.Value)
                    .ToList();
                return preferences.Count > 0 ? preferences.Average() : null;
            }
            double range = System.Math.Max(
                SidePreference(first, second) ?? 0,
                SidePreference(second, first) ?? 0);
            return (ushort)System.Math.Clamp(range, 0, ushort.MaxValue);
        }
    }
}
