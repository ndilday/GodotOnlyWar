using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.FactionBehaviors;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Evaluates proximity-based mob morale for any faction with MobMentality. It does not infer
    /// identity from population, hostility, indelibility, or a display name.
    /// </summary>
    internal static class MobMoraleSupportEvaluator
    {
        internal static float ComputeSupport(
            BattleSquad squad,
            IEnumerable<BattleSquad> activeFriendly,
            IEnumerable<BattleSquad> allFriendly,
            BattleGridManager grid,
            FactionBehaviorRulesProfile rules,
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
                float health = other.Soldiers.Count == 0
                    ? 0f
                    : Math.Clamp(other.AbleSoldiers.Count / (float)other.Soldiers.Count, 0f, 1f);
                float value = (float)(rules.MoraleNearbyMobSupport * health)
                    - (float)(rules.MoraleCasualtyPenalty * (1f - health));
                if (other.WithdrawalRole == WithdrawalRole.Routing)
                    value -= (float)rules.MoraleRoutPenalty;
                if (grid.GetMinimumDistanceBetweenSquads(squad, other)
                    > MoraleConstants.VisualRange * 0.5f)
                    value -= (float)rules.MoraleSeparatedPenalty;
                if (other.SquadProvidesCommandAura && genericCommandAuraSupport <= 0f)
                    value += (float)(rules.MoraleLivingLeaderSupport * health);
                return value;
            });

            bool anyCommandProvider = (allFriendly ?? activeFriendly ?? Enumerable.Empty<BattleSquad>())
                .Any(candidate => candidate?.SquadProvidesCommandAura == true);
            if (!anyCommandProvider && genericCommandAuraSupport >= 0f)
                support -= (float)rules.MoraleCommandLossPenalty;

            return Math.Clamp(support, -1f, (float)rules.MoraleMaximumSupport);
        }
    }
}
