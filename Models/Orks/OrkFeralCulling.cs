using System;
using OnlyWar.Models;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Planets;

namespace OnlyWar.Models.Orks
{
    public sealed record OrkFeralCullingResult(
        bool WasBeliefEligible,
        bool WasFalsePositive,
        long PopulationRemoved,
        double ConsolidationRemoved,
        bool RequestedOutsideHelp);

    [Obsolete("Use DormantPopulationCulling.")]
    public static class OrkFeralCullingRules
    {
        public static bool CanTarget(RegionFaction target, FactionIntelBelief belief) =>
            DormantPopulationCulling.CanTarget(target, belief);

        public static OrkFeralCullingResult Resolve(
            RegionFaction target,
            FactionIntelBelief belief,
            OrkCampaignRulesProfile profile,
            long effectivePdfBattleValue,
            bool falsePositive = false)
        {
            DormantPopulationCullingResult result = DormantPopulationCulling.Resolve(
                target, belief, profile, effectivePdfBattleValue, falsePositive);
            return new OrkFeralCullingResult(
                result.WasBeliefEligible,
                result.WasFalsePositive,
                result.PopulationRemoved,
                result.ConsolidationRemoved,
                result.RequestedOutsideHelp);
        }
    }
}
