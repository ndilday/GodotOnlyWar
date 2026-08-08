using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Missions
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
    /// PHASE 7 (Design/Reference/EngagementScoringOverhaul.md): each side's preference is now the
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
            IReadOnlyList<BattleSquad> missionSquads,
            IReadOnlyList<BattleSquad> opposingSquads,
            float marginOfSuccess,
            IRNG random)
        {
            float rangeModifier = GaussianCalculator.ApproximateNormalCDF(marginOfSuccess);
            // RNG-STREAM ANCHOR, and nothing else. Phase 6 drew one representative member per side
            // to stand in for that side's target profile, opposing-side first, to preserve the RNG
            // order this logic had when it lived inline in MeetingEngagementMissionStep. Phase 7
            // derives each preference against the WHOLE opposing force, so the draws no longer feed
            // anything -- but dropping them would shift every seeded mission battle's RNG stream,
            // mixing a wholesale re-baseline into a change whose only intended effect is the range.
            // Keeping them makes any divergence below attributable to the opening range itself.
            _ = opposingSquads.First().GetRandomSquadMember(random);
            _ = missionSquads.First().GetRandomSquadMember(random);
            double missionRange = missionSquads.Average(
                squad => squad.GetPreferredOpeningRange(opposingSquads));
            double opposingRange = opposingSquads.Average(
                squad => squad.GetPreferredOpeningRange(missionSquads));
            return (ushort)(opposingRange + (missionRange - opposingRange) * rangeModifier);
        }
    }
}
