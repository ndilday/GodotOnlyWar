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
    /// from us is in OURS. Whether the two are one currency depends on whether battle value is
    /// calibrated ACROSS factions.</para>
    ///
    /// <para>THE RULE (2026-10-12, product owner): battle value parity. One unit of enemy battle value
    /// is worth one unit of ours, so <see cref="EnemyValueRate"/> is 1, and an exchange is valued by
    /// the battle value each side loses. Battle value is now calibrated across factions from fights
    /// (the gauntlet: what an exchange is worth is exactly what it measures), so a trade that the
    /// calibration calls even is even to the planner.</para>
    ///
    /// <para>HISTORY. From 2026-10-05 to 2026-10-12 the rate was headcount parity: the planning
    /// squad's mean soldier value over the enemy's, so one average enemy soldier was worth one of
    /// ours. It was introduced while cross-faction values were still uncalibrated -- recalibrating
    /// Space Marines from about 9 to 86 per soldier had set the cult running at Scouts it had been
    /// shooting at (Design/Active/BattleValueCalibration.md §7.16). Once values were calibrated it
    /// made every cheap, numerous force refuse trades the calibration calls fair: an Insurrectionist
    /// Mob valued a marine (100) as one Insurrectionist (5), so standing and running in were both
    /// heavy losses, and ten Mobs stood 200 yards from one Tactical squad for 400 turns (§7.24).</para>
    ///
    /// <para><see cref="ScoreUnit"/> still quotes thresholds per average soldier of the planning
    /// squad, so a fixed band means the same thing to a marine squad and a PDF one.</para>
    /// </summary>
    internal static class EngagementCurrency
    {
        /// <summary>
        /// Own battle value per unit of enemy battle value: multiply an enemy-priced term by this
        /// to put it in the planning squad's currency. Battle value parity: 1.
        /// </summary>
        internal const float EnemyValueRate = 1f;

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
        /// One average soldier of the planning squad, the unit decision thresholds are quoted in.
        /// When the squad has no value, the enemy's average soldier.
        /// </summary>
        internal static float ScoreUnit(
            BattleSquad squad,
            IEnumerable<BattleSquad> enemies)
        {
            float own = MeanSoldierValue([squad]);
            return own > 0 ? own : MeanSoldierValue(enemies);
        }
    }
}
