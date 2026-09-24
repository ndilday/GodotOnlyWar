using OnlyWar.Domain;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.FactionBehaviors;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Battles
{
    /// <summary>
    /// Evaluates proximity-based mob morale for any faction with MobMentality. It does not infer
    /// identity from population, hostility, indelibility, or a display name.
    /// </summary>
    internal static class MobMoraleSupportEvaluator
    {
        /// <param name="startingAbleCount">
        /// Battle-start able strength per squad. A nearby mob's health is measured against it,
        /// because the dead leave <see cref="BattleSquad.Soldiers"/> at end of turn and a
        /// current-roster denominator would read a half-dead mob as whole.
        /// </param>
        /// <param name="routingAtTurnStart">
        /// Squads that were Routing at the start of this turn. A squad that breaks during this
        /// turn's checks only discourages its neighbours next turn, matching the routing-visible
        /// shock term, so the result does not depend on the order squads are checked in.
        /// </param>
        internal static float ComputeSupport(
            BattleSquad squad,
            IEnumerable<BattleSquad> activeFriendly,
            IEnumerable<BattleSquad> allFriendly,
            BattleGridManager grid,
            FactionBehaviorRulesProfile rules,
            Func<BattleSquad, int> startingAbleCount,
            ISet<int> routingAtTurnStart,
            float genericCommandAuraSupport = 0f)
        {
            if (!FactionCapabilities.HasMobMentality(squad?.Faction)
                || grid == null || rules == null) return 0f;

            List<BattleSquad> nearby = (activeFriendly ?? Enumerable.Empty<BattleSquad>())
                .Where(other => other != null
                    && other != squad
                    && other.Status == BattleSquadStatus.Active
                    && other.AbleSoldiers.Count > 0
                    && grid.GetMinimumDistanceBetweenSquads(squad, other) <= MoraleConstants.VisualRange)
                .ToList();

            float support = nearby.Sum(other =>
            {
                int starting = startingAbleCount?.Invoke(other) ?? other.AbleSoldiers.Count;
                float health = starting <= 0
                    ? 0f
                    : Math.Clamp(other.AbleSoldiers.Count / (float)starting, 0f, 1f);
                float value = (float)(rules.MoraleNearbyMobSupport * health)
                    - (float)(rules.MoraleCasualtyPenalty * (1f - health));
                if (routingAtTurnStart?.Contains(other.Id) == true)
                    value -= (float)rules.MoraleRoutPenalty;
                if (grid.GetMinimumDistanceBetweenSquads(squad, other)
                    > MoraleConstants.VisualRange * 0.5f)
                    value -= (float)rules.MoraleSeparatedPenalty;
                if (other.SquadProvidesCommandAura && genericCommandAuraSupport <= 0f)
                    value += (float)(rules.MoraleLivingLeaderSupport * health);
                return value;
            });

            if (CommandWasLost(squad, allFriendly ?? activeFriendly))
                support -= (float)rules.MoraleCommandLossPenalty;

            return Math.Clamp(support, -1f, (float)rules.MoraleMaximumSupport);
        }

        /// <summary>
        /// True only when the side fielded a command provider and every one has been destroyed.
        /// A warband that never had a boss has lost nothing and takes no penalty. The liveness
        /// rule matches <see cref="CommandAuraEvaluator"/>: a disengaged provider is alive.
        /// The penalty adds to that evaluator's generic loss term on purpose, because a mob
        /// leans harder on its bosses than a disciplined force leans on its officers.
        /// </summary>
        private static bool CommandWasLost(BattleSquad squad, IEnumerable<BattleSquad> allFriendly)
        {
            bool fielded = false;
            foreach (BattleSquad provider in allFriendly ?? Enumerable.Empty<BattleSquad>())
            {
                if (provider == null || provider == squad || !provider.SquadProvidesCommandAura)
                    continue;
                fielded = true;
                if (provider.Status != BattleSquadStatus.Eliminated && provider.AbleSoldiers.Count > 0)
                    return false;
            }
            return fielded;
        }
    }
}
