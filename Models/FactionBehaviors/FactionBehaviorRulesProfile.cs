using OnlyWar.Helpers;
using System;

namespace OnlyWar.Models.FactionBehaviors
{
    /// <summary>
    /// Data-owned numeric tuning for the reusable ghost, dormant-population, invasion, and mob
    /// capabilities. A profile is not an Ork identity; it can be assigned to any faction that has
    /// the corresponding capability flags.
    /// </summary>
    public class FactionBehaviorRulesProfile
    {
        public string Key { get; }
        public double GhostSourceChancePerEmptyTile { get; }
        public int MinimumGhostSourcesPerSector { get; }
        public double WeeklyConsolidationSigmaDivisor { get; }
        public double WeeklyConsolidationDrift { get; }
        public double MobilizationMedian { get; }
        public double MobilizationSigma { get; }
        public double MobilizationMinimum { get; }
        public double MobilizationMaximum { get; }
        public double DefendedLandingRatio { get; }
        public long UndefendedLandingBattleValue { get; }
        public long SuccessorGenerationMinimumBattleValue { get; }
        public double SuccessorMergeLeaderLoss { get; }
        public double TravelMultiplier { get; }
        public double GhostLogisticGrowthRate { get; }
        public double OccupiedCivilianDeclineRate { get; }
        public double ExceptionalAssassinationMargin { get; }
        public double PublicGrowthMultiplier { get; }
        public double DormantGrowthMultiplier { get; }
        public long DormantEmergenceMinimumPopulation { get; }
        public double DormantEmergenceChance { get; }
        public double DormantCullingPopulationReductionFraction { get; }
        public double DormantCullingConsolidationReductionFraction { get; }
        public double DormantCullingOutsideHelpEffectivePdfFloor { get; }
        public double DormantCullingFalsePositiveCapacityCost { get; }
        public double MoraleNearbyMobSupport { get; }
        public double MoraleLivingLeaderSupport { get; }
        public double MoraleCasualtyPenalty { get; }
        public double MoraleRoutPenalty { get; }
        public double MoraleSeparatedPenalty { get; }
        public double MoraleCommandLossPenalty { get; }
        public double MoraleMaximumSupport { get; }
        public double DormantInitialBeliefChance { get; }
        public double DormantInitialBeliefEvidence { get; }

        // Compatibility aliases are intentionally confined to the generic profile boundary. Old
        // saves and older callers can continue to read the old column vocabulary while all new
        // production consumers use the capability-owned names above.
        [Obsolete("Use TravelMultiplier.")] public double OrkTravelMultiplier => TravelMultiplier;
        [Obsolete("Use DormantGrowthMultiplier.")] public double FeralGrowthMultiplier => DormantGrowthMultiplier;
        [Obsolete("Use DormantEmergenceMinimumPopulation.")] public long FeralEmergenceMinimumPopulation => DormantEmergenceMinimumPopulation;
        [Obsolete("Use DormantEmergenceChance.")] public double FeralEmergenceChance => DormantEmergenceChance;
        [Obsolete("Use DormantCullingPopulationReductionFraction.")] public double CullingPopulationReductionFraction => DormantCullingPopulationReductionFraction;
        [Obsolete("Use DormantCullingConsolidationReductionFraction.")] public double CullingConsolidationReductionFraction => DormantCullingConsolidationReductionFraction;
        [Obsolete("Use DormantCullingOutsideHelpEffectivePdfFloor.")] public double CullingOutsideHelpEffectivePdfFloor => DormantCullingOutsideHelpEffectivePdfFloor;
        [Obsolete("Use DormantCullingFalsePositiveCapacityCost.")] public double CullingFalsePositiveCapacityCost => DormantCullingFalsePositiveCapacityCost;
        [Obsolete("Use MoraleLivingLeaderSupport.")] public double MoraleLivingWarbossSupport => MoraleLivingLeaderSupport;
        [Obsolete("Use DormantInitialBeliefChance.")] public double FeralInitialBeliefChance => DormantInitialBeliefChance;
        [Obsolete("Use DormantInitialBeliefEvidence.")] public double FeralInitialBeliefEvidence => DormantInitialBeliefEvidence;

        public FactionBehaviorRulesProfile(
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
            double travelMultiplier,
            double ghostLogisticGrowthRate,
            double occupiedCivilianDeclineRate,
            double exceptionalAssassinationMargin,
            double publicGrowthMultiplier,
            double dormantGrowthMultiplier,
            long dormantEmergenceMinimumPopulation,
            double dormantEmergenceChance,
            double dormantCullingPopulationReductionFraction,
            double dormantCullingConsolidationReductionFraction,
            double dormantCullingOutsideHelpEffectivePdfFloor,
            double dormantCullingFalsePositiveCapacityCost,
            double moraleNearbyMobSupport,
            double moraleLivingLeaderSupport,
            double moraleCasualtyPenalty,
            double moraleRoutPenalty,
            double moraleSeparatedPenalty,
            double moraleCommandLossPenalty,
            double moraleMaximumSupport,
            double dormantInitialBeliefChance = 0.35,
            double dormantInitialBeliefEvidence = 3.0)
        {
            Key = key;
            GhostSourceChancePerEmptyTile = ghostSourceChancePerEmptyTile;
            MinimumGhostSourcesPerSector = minimumGhostSourcesPerSector;
            WeeklyConsolidationSigmaDivisor = weeklyConsolidationSigmaDivisor;
            WeeklyConsolidationDrift = weeklyConsolidationDrift;
            MobilizationMedian = mobilizationMedian;
            MobilizationSigma = mobilizationSigma;
            MobilizationMinimum = mobilizationMinimum;
            MobilizationMaximum = mobilizationMaximum;
            DefendedLandingRatio = defendedLandingRatio;
            UndefendedLandingBattleValue = undefendedLandingBattleValue;
            SuccessorGenerationMinimumBattleValue = successorGenerationMinimumBattleValue;
            SuccessorMergeLeaderLoss = successorMergeLeaderLoss;
            TravelMultiplier = travelMultiplier;
            GhostLogisticGrowthRate = ghostLogisticGrowthRate;
            OccupiedCivilianDeclineRate = occupiedCivilianDeclineRate;
            ExceptionalAssassinationMargin = exceptionalAssassinationMargin;
            PublicGrowthMultiplier = publicGrowthMultiplier;
            DormantGrowthMultiplier = dormantGrowthMultiplier;
            DormantEmergenceMinimumPopulation = dormantEmergenceMinimumPopulation;
            DormantEmergenceChance = dormantEmergenceChance;
            DormantCullingPopulationReductionFraction = dormantCullingPopulationReductionFraction;
            DormantCullingConsolidationReductionFraction = dormantCullingConsolidationReductionFraction;
            DormantCullingOutsideHelpEffectivePdfFloor = dormantCullingOutsideHelpEffectivePdfFloor;
            DormantCullingFalsePositiveCapacityCost = dormantCullingFalsePositiveCapacityCost;
            MoraleNearbyMobSupport = moraleNearbyMobSupport;
            MoraleLivingLeaderSupport = moraleLivingLeaderSupport;
            MoraleCasualtyPenalty = moraleCasualtyPenalty;
            MoraleRoutPenalty = moraleRoutPenalty;
            MoraleSeparatedPenalty = moraleSeparatedPenalty;
            MoraleCommandLossPenalty = moraleCommandLossPenalty;
            MoraleMaximumSupport = moraleMaximumSupport;
            DormantInitialBeliefChance = dormantInitialBeliefChance;
            DormantInitialBeliefEvidence = dormantInitialBeliefEvidence;
        }

        public virtual void Validate()
        {
            if (string.IsNullOrWhiteSpace(Key)
                || !IsFinite(GhostSourceChancePerEmptyTile)
                || !IsFinite(WeeklyConsolidationSigmaDivisor)
                || !IsFinite(WeeklyConsolidationDrift)
                || !IsFinite(MobilizationMedian)
                || !IsFinite(MobilizationSigma)
                || !IsFinite(MobilizationMinimum)
                || !IsFinite(MobilizationMaximum)
                || !IsFinite(DefendedLandingRatio)
                || !IsFinite(SuccessorMergeLeaderLoss)
                || !IsFinite(TravelMultiplier)
                || !IsFinite(GhostLogisticGrowthRate)
                || !IsFinite(OccupiedCivilianDeclineRate)
                || !IsFinite(ExceptionalAssassinationMargin)
                || !IsFinite(PublicGrowthMultiplier)
                || !IsFinite(DormantGrowthMultiplier)
                || !IsFinite(DormantEmergenceChance)
                || !IsFinite(DormantCullingPopulationReductionFraction)
                || !IsFinite(DormantCullingConsolidationReductionFraction)
                || !IsFinite(DormantCullingOutsideHelpEffectivePdfFloor)
                || !IsFinite(DormantCullingFalsePositiveCapacityCost)
                || !IsFinite(MoraleNearbyMobSupport)
                || !IsFinite(MoraleLivingLeaderSupport)
                || !IsFinite(MoraleCasualtyPenalty)
                || !IsFinite(MoraleRoutPenalty)
                || !IsFinite(MoraleSeparatedPenalty)
                || !IsFinite(MoraleCommandLossPenalty)
                || !IsFinite(MoraleMaximumSupport)
                || !IsFinite(DormantInitialBeliefChance)
                || !IsFinite(DormantInitialBeliefEvidence)
                || GhostSourceChancePerEmptyTile < 0 || GhostSourceChancePerEmptyTile > 1
                || MinimumGhostSourcesPerSector < 0
                || WeeklyConsolidationSigmaDivisor <= 0
                || MobilizationMedian < 0 || MobilizationMedian > 1
                || MobilizationSigma < 0
                || MobilizationMinimum < 0 || MobilizationMaximum > 1
                || MobilizationMinimum > MobilizationMaximum
                || DefendedLandingRatio <= 0 || UndefendedLandingBattleValue < 0
                || SuccessorGenerationMinimumBattleValue < 0 || SuccessorMergeLeaderLoss < 0
                || SuccessorMergeLeaderLoss >= 1 || TravelMultiplier <= 0
                || GhostLogisticGrowthRate < 0 || OccupiedCivilianDeclineRate < 0
                || ExceptionalAssassinationMargin <= 0 || PublicGrowthMultiplier < 0
                || DormantGrowthMultiplier < 0 || DormantEmergenceMinimumPopulation < 0
                || DormantEmergenceChance < 0 || DormantEmergenceChance > 1
                || DormantCullingPopulationReductionFraction < 0 || DormantCullingPopulationReductionFraction > 1
                || DormantCullingConsolidationReductionFraction < 0 || DormantCullingConsolidationReductionFraction > 1
                || DormantCullingOutsideHelpEffectivePdfFloor < 0
                || DormantCullingFalsePositiveCapacityCost < 0
                || MoraleNearbyMobSupport < 0 || MoraleLivingLeaderSupport < 0
                || MoraleCasualtyPenalty < 0 || MoraleRoutPenalty < 0
                || MoraleSeparatedPenalty < 0 || MoraleCommandLossPenalty < 0
                || MoraleMaximumSupport < 0
                || DormantInitialBeliefChance < 0 || DormantInitialBeliefChance > 1
                || DormantInitialBeliefEvidence < 0
                || DormantInitialBeliefEvidence > FactionIntelligenceRules.MaxEvidence)
            {
                throw new InvalidOperationException(
                    $"Faction behavior profile '{Key}' contains an invalid value.");
            }
        }

        private static bool IsFinite(double value) => double.IsFinite(value);
    }
}
