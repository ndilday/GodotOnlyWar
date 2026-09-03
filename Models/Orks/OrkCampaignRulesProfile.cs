using OnlyWar.Models.FactionBehaviors;

namespace OnlyWar.Models.Orks
{
    /// <summary>Compatibility projection for the legacy Ork rules table and callers.</summary>
    [System.Obsolete("Use FactionBehaviorRulesProfile.")]
    public sealed class OrkCampaignRulesProfile : FactionBehaviorRulesProfile
    {
        public OrkCampaignRulesProfile(FactionBehaviorRulesProfile profile)
            : this(profile?.Key,
                profile?.GhostSourceChancePerEmptyTile ?? 0,
                profile?.MinimumGhostSourcesPerSector ?? 0,
                profile?.WeeklyConsolidationSigmaDivisor ?? 100,
                profile?.WeeklyConsolidationDrift ?? 0,
                profile?.MobilizationMedian ?? 0,
                profile?.MobilizationSigma ?? 0,
                profile?.MobilizationMinimum ?? 0,
                profile?.MobilizationMaximum ?? 1,
                profile?.DefendedLandingRatio ?? 2,
                profile?.UndefendedLandingBattleValue ?? 0,
                profile?.SuccessorGenerationMinimumBattleValue ?? 0,
                profile?.SuccessorMergeLeaderLoss ?? 0,
                profile?.TravelMultiplier ?? 1,
                profile?.GhostLogisticGrowthRate ?? 0,
                profile?.OccupiedCivilianDeclineRate ?? 0,
                profile?.ExceptionalAssassinationMargin ?? 1,
                profile?.PublicGrowthMultiplier ?? 0,
                profile?.DormantGrowthMultiplier ?? 0,
                profile?.DormantEmergenceMinimumPopulation ?? 0,
                profile?.DormantEmergenceChance ?? 0,
                profile?.DormantCullingPopulationReductionFraction ?? 0,
                profile?.DormantCullingConsolidationReductionFraction ?? 0,
                profile?.DormantCullingOutsideHelpEffectivePdfFloor ?? 0,
                profile?.DormantCullingFalsePositiveCapacityCost ?? 0,
                profile?.MoraleNearbyMobSupport ?? 0,
                profile?.MoraleLivingLeaderSupport ?? 0,
                profile?.MoraleCasualtyPenalty ?? 0,
                profile?.MoraleRoutPenalty ?? 0,
                profile?.MoraleSeparatedPenalty ?? 0,
                profile?.MoraleCommandLossPenalty ?? 0,
                profile?.MoraleMaximumSupport ?? 0,
                profile?.DormantInitialBeliefChance ?? 0.35,
                profile?.DormantInitialBeliefEvidence ?? 3.0)
        {
        }

        public OrkCampaignRulesProfile(
            string key,
            double ghostSourceChancePerEmptyTile,
            int minimumGhostSourcesPerSector,
            double weeklyConsolidationSigmaDivisor,
            double weeklyConsolidationDrift,
            double mobilizationMedian,
            double mobilizationSigma,
            double mobilizationMinimum,
            double mobilizationMaximum,
            double defendedLandingRatio,
            long undefendedLandingBattleValue,
            long successorGenerationMinimumBattleValue,
            double successorMergeLeaderLoss,
            double orkTravelMultiplier,
            double ghostLogisticGrowthRate,
            double occupiedCivilianDeclineRate,
            double exceptionalAssassinationMargin,
            double publicGrowthMultiplier,
            double feralGrowthMultiplier,
            long feralEmergenceMinimumPopulation,
            double feralEmergenceChance,
            double cullingPopulationReductionFraction,
            double cullingConsolidationReductionFraction,
            double cullingOutsideHelpEffectivePdfFloor,
            double cullingFalsePositiveCapacityCost,
            double moraleNearbyMobSupport,
            double moraleLivingWarbossSupport,
            double moraleCasualtyPenalty,
            double moraleRoutPenalty,
            double moraleSeparatedPenalty,
            double moraleCommandLossPenalty,
            double moraleMaximumSupport,
            double feralInitialBeliefChance = 0.35,
            double feralInitialBeliefEvidence = 3.0)
            : base(key, ghostSourceChancePerEmptyTile, minimumGhostSourcesPerSector,
                weeklyConsolidationSigmaDivisor, weeklyConsolidationDrift, mobilizationMedian,
                mobilizationSigma, mobilizationMinimum, mobilizationMaximum, defendedLandingRatio,
                undefendedLandingBattleValue, successorGenerationMinimumBattleValue,
                successorMergeLeaderLoss, orkTravelMultiplier, ghostLogisticGrowthRate,
                occupiedCivilianDeclineRate, exceptionalAssassinationMargin, publicGrowthMultiplier,
                feralGrowthMultiplier, feralEmergenceMinimumPopulation, feralEmergenceChance,
                cullingPopulationReductionFraction, cullingConsolidationReductionFraction,
                cullingOutsideHelpEffectivePdfFloor, cullingFalsePositiveCapacityCost,
                moraleNearbyMobSupport, moraleLivingWarbossSupport, moraleCasualtyPenalty,
                moraleRoutPenalty, moraleSeparatedPenalty, moraleCommandLossPenalty,
                moraleMaximumSupport, feralInitialBeliefChance, feralInitialBeliefEvidence)
        {
        }
    }
}
