using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Models.Orks
{
    /// <summary>
    /// Compatibility facade for the former Ork-specific state vocabulary. New code uses
    /// DormantPopulationRules, GhostPopulationSource, and StrategicInvasionForce directly.
    /// </summary>
    [System.Obsolete("Use DormantPopulationRules.")]
    public static class OrkInfestationRules
    {
        public const double GhostSourceChancePerEmptyTile = 0.0001;
        public const int MinimumGhostSourcesPerSector = 1;
        public const double WeeklyConsolidationSigmaDivisor = 100.0;
        public const double WeeklyConsolidationDrift = 0.001;
        public const double MobilizationMedian = 0.60;
        public const double MobilizationSigma = 0.10;
        public const double MobilizationMinimum = 0.25;
        public const double MobilizationMaximum = 0.90;
        public const double DefendedLandingRatio = 2.0;
        public const long UndefendedLandingBattleValue = 1000;
        public const long SuccessorGenerationMinimumBattleValue = 10000;
        public const double SuccessorMergeLeaderLoss = 0.10;
        public const double OrkTravelMultiplier = 1.10;
        public const double GhostLogisticGrowthRate = 0.0006;
        public const double OccupiedCivilianDeclineRate = 0.0006;
        public const double ExceptionalAssassinationMargin = 3.0;
        public const double PublicGrowthMultiplier = 2.0;
        public const double FeralGrowthMultiplier = 0.10;

        public static double UpdateConsolidation(double current, double zValue) =>
            DormantPopulationRules.UpdateConsolidation(current, zValue);

        public static double UpdateConsolidation(FactionBehaviorRulesProfile profile,
            double current, double zValue) =>
            DormantPopulationRules.UpdateConsolidation(profile, current, zValue);

        public static double MobilizationFraction(double zValue) =>
            DormantPopulationRules.MobilizationFraction(zValue);

        public static double MobilizationFraction(FactionBehaviorRulesProfile profile,
            double zValue) =>
            DormantPopulationRules.MobilizationFraction(profile, zValue);

        public static double FeralEfficiency(bool isPublic) =>
            DormantPopulationRules.GrowthEfficiency(isPublic);

        public static double FeralEfficiency(FactionBehaviorRulesProfile profile, bool isPublic) =>
            DormantPopulationRules.GrowthEfficiency(profile, isPublic);
    }

    /// <summary>Compatibility wrapper for old save/test code.</summary>
    [System.Obsolete("Use GhostPopulationSource.")]
    public sealed class OrkGhostSource : GhostPopulationSource
    {
        public OrkGhostSource(int id, Coordinate position, PlanetTemplate worldType,
            long population, long populationCapacity, double consolidation, Faction faction = null)
            : base(id, position, worldType, population, populationCapacity, consolidation, faction)
        {
        }
    }

    /// <summary>Compatibility wrapper for old save/test code.</summary>
    [System.Obsolete("Use StrategicInvasionForce.")]
    public sealed class OrkWaaagh : StrategicInvasionForce
    {
        public Squad WarbossSquad => CommandSquad;
        public ISoldier Warboss => StrategicCommander;

        public OrkWaaagh(long id, Faction faction, Squad commandSquad,
            Region currentRegion, Planet originPlanet)
            : base(id, faction, commandSquad, currentRegion, originPlanet)
        {
        }
    }

    /// <summary>Compatibility DTO for legacy save tables.</summary>
    [System.Obsolete("Use StrategicInvasionForceSaveData.")]
    public sealed class OrkWaaaghSaveData : StrategicInvasionForceSaveData
    {
    }
}
