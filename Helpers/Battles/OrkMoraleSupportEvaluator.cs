using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Orks;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Computes only the Ork side's internal morale support. It never contributes to the enemy's
    /// stress. The same function is used by live morale and the RNG-free withdrawal forecast.
    /// </summary>
    [Obsolete("Use MobMoraleSupportEvaluator.")]
    internal static class OrkMoraleSupportEvaluator
    {
        internal static float ComputeSupport(
            BattleSquad squad,
            IEnumerable<BattleSquad> activeFriendly,
            IEnumerable<BattleSquad> allFriendly,
            BattleGridManager grid,
            OrkCampaignRulesProfile rules,
            float genericCommandAuraSupport = 0f)
        {
            return MobMoraleSupportEvaluator.ComputeSupport(
                squad, activeFriendly, allFriendly, grid, rules, genericCommandAuraSupport);
        }

        internal static bool IsOrk(BattleSquad squad) =>
            FactionCapabilities.HasMobMentality(squad?.Faction);
    }
}
