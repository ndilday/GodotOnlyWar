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
    /// <see cref="BattleSquad.GetPreferredOpeningRange"/> is the right input rather than
    /// GetPreferredEngagementRange, because it disambiguates a zero standoff range by cause — a
    /// missile launcher that cannot reliably hit still wants to open far and plink, while a weapon
    /// that cannot wound the target at any range gains nothing by standing off.
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
            // Representative members stand in for each side's target profile. Drawn opposing-side
            // first to preserve the RNG order this logic had when it lived inline in
            // MeetingEngagementMissionStep, so that path's seeded baselines do not move.
            BattleSoldier opposingSoldier = opposingSquads.First().GetRandomSquadMember(random);
            BattleSoldier missionSoldier = missionSquads.First().GetRandomSquadMember(random);
            double missionRange = missionSquads.Average(squad => squad.GetPreferredOpeningRange(
                opposingSoldier.Soldier.Size,
                opposingSoldier.Armor.Template.ArmorProvided,
                opposingSoldier.Soldier.Constitution,
                opposingSoldier.Soldier.Template.Species.RangedEvasion));
            double opposingRange = opposingSquads.Average(squad => squad.GetPreferredOpeningRange(
                missionSoldier.Soldier.Size,
                missionSoldier.Armor.Template.ArmorProvided,
                missionSoldier.Soldier.Constitution,
                missionSoldier.Soldier.Template.Species.RangedEvasion));
            return (ushort)(opposingRange + (missionRange - opposingRange) * rangeModifier);
        }
    }
}
