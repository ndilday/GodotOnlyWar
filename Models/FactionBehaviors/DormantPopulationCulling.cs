using OnlyWar.Models.Planets;
using System;

namespace OnlyWar.Models.FactionBehaviors
{
    public sealed record DormantPopulationCullingResult(
        bool WasBeliefEligible,
        bool WasFalsePositive,
        long PopulationRemoved,
        double ConsolidationRemoved,
        bool RequestedOutsideHelp);

    /// <summary>
    /// Culling targets dormant activity and intelligence, never a generic hidden/concealed flag.
    /// </summary>
    public static class DormantPopulationCulling
    {
        public static bool CanTarget(RegionFaction target, FactionIntelBelief belief) =>
            target?.PlanetFaction?.Faction != null
            && FactionCapabilities.HasDormantPopulations(target.PlanetFaction.Faction)
            && target.StrategicInvasionForceId == null
            && target.Population > 0
            && belief?.Level >= IntelLevel.Confirmed;

        public static DormantPopulationCullingResult Resolve(
            RegionFaction target,
            FactionIntelBelief belief,
            FactionBehaviorRulesProfile profile,
            long effectivePdfBattleValue,
            bool falsePositive = false)
        {
            bool eligible = CanTarget(target, belief);
            if (!eligible || falsePositive)
            {
                return new DormantPopulationCullingResult(
                    eligible,
                    falsePositive,
                    0,
                    0,
                    effectivePdfBattleValue < profile.DormantCullingOutsideHelpEffectivePdfFloor);
            }

            long removed = Math.Min(target.Population, Math.Max(0L, (long)Math.Floor(
                target.Population * profile.DormantCullingPopulationReductionFraction)));
            double consolidationRemoved = target.DormantConsolidation
                * profile.DormantCullingConsolidationReductionFraction;
            return new DormantPopulationCullingResult(
                true,
                false,
                removed,
                consolidationRemoved,
                effectivePdfBattleValue < profile.DormantCullingOutsideHelpEffectivePdfFloor);
        }
    }
}
