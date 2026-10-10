using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Battles
{
    /// <summary>
    /// The one exchange rate between the two battle-value currencies an engagement decision reads.
    ///
    /// <para>WHY THERE ARE TWO CURRENCIES. Every removal estimate is priced in the battle value of
    /// the soldiers it removes: what we take from the enemy is in ENEMY battle value, what they take
    /// from us is in OURS. Battle value is calibrated within a faction against the PDF reference, so
    /// it says honestly which of our soldiers matters more and which of theirs -- but comparing a
    /// sum in one currency with a sum in the other lets the ratio between the two factions' values
    /// decide the option. Recalibrating Space Marines from about 9 to 86 per soldier (2026-10,
    /// Design/Active/BattleValueCalibration.md §7.16) multiplied every term priced in Scout battle
    /// value by ten against every term priced in the Neophyte Hybrids' own, and the cult started
    /// running at Scouts it had been shooting at.</para>
    ///
    /// <para>THE RULE. A decision is scored in the planning squad's own currency. Every term priced
    /// in enemy battle value is converted at <see cref="EnemyValueRate"/>: the planning squad's
    /// mean soldier value over the opposing force's mean soldier value. One average enemy soldier is
    /// worth one average soldier of ours -- headcount parity between the sides, the same footing
    /// the withdrawal and morale shares stand on (see ForceShareScaleFreeTests) -- while relative
    /// value WITHIN each side still decides which soldier, or which enemy squad, matters more.
    /// Multiplying either side's battle values by a constant then multiplies every term of a score
    /// by the same factor, and cannot change which option wins.</para>
    ///
    /// <para>Where two forces have the same mean soldier value the rate is exactly 1, so every
    /// fixture built from one template family scores exactly as it did before the conversion.</para>
    /// </summary>
    internal static class EngagementCurrency
    {
        /// <summary>
        /// Mean battle value of the able soldiers of <paramref name="squads"/>, in the
        /// <see cref="SquadPlanningServices.BattleValueOf"/> pricing the removal estimates use.
        /// Zero when there is nobody to price.
        /// </summary>
        internal static float MeanSoldierValue(IEnumerable<BattleSquad> squads)
        {
            float total = 0;
            int count = 0;
            foreach (BattleSoldier soldier in (squads ?? [])
                .Where(squad => squad != null)
                .SelectMany(squad => squad.AbleSoldiers))
            {
                total += SquadPlanningServices.BattleValueOf(soldier);
                count++;
            }
            return count > 0 ? total / count : 0;
        }

        /// <summary>
        /// Own battle value per unit of enemy battle value: multiply an enemy-priced term by this
        /// to put it in the planning squad's currency. 1 when either side has no positive value,
        /// since there is then nothing on that side to convert.
        /// </summary>
        internal static float EnemyValueRate(
            BattleSquad squad,
            IEnumerable<BattleSquad> enemies) =>
            Rate(MeanSoldierValue([squad]), MeanSoldierValue(enemies));

        /// <summary>
        /// One average soldier of the currency a decision is scored in: the planning squad's own
        /// mean soldier value. When the squad has no value, <see cref="EnemyValueRate"/> leaves
        /// enemy terms unconverted, so the score is in enemy battle value and so is its unit.
        /// </summary>
        internal static float ScoreUnit(
            BattleSquad squad,
            IEnumerable<BattleSquad> enemies)
        {
            float own = MeanSoldierValue([squad]);
            return own > 0 ? own : MeanSoldierValue(enemies);
        }

        internal static float Rate(float ownMeanValue, float enemyMeanValue) =>
            float.IsFinite(ownMeanValue)
                && float.IsFinite(enemyMeanValue)
                && ownMeanValue > 0
                && enemyMeanValue > 0
                    ? ownMeanValue / enemyMeanValue
                    : 1f;
    }
}
