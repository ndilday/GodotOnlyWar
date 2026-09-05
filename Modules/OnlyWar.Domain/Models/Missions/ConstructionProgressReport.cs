using OnlyWar.Models.Planets;
using System.Collections.Generic;

namespace OnlyWar.Models.Missions
{
    /// <summary>
    /// What one squad-borne construction order actually accomplished this turn. Construction has no
    /// completion event to report - defense levels accrue fractionally forever (RegionFaction.Entrenchment
    /// and friends are doubles) - so the only way a player can tell a fortification order is working is
    /// to be shown the before/after levels and the rate. Produced by MissionTurnProcessor and rendered
    /// into the end-of-turn report by ConstructionReportBuilder.
    /// </summary>
    public sealed class ConstructionProgressReport
    {
        public DefenseType ConstructionType { get; }
        public RegionFaction RegionFaction { get; }
        public IReadOnlyList<string> SquadNames { get; }
        public bool IsPlayerConstruction { get; }
        // Levels on the building faction's own RegionFaction before and after this turn's work -
        // its contribution, which is what the squad actually moved.
        public double LevelBefore { get; }
        public double LevelAfter { get; }
        // The side's pooled position (RegionDefenses) as it stood before this week's work. There is
        // deliberately no matching "after" field: construction resolves early in the turn, and
        // allied building, decay, sabotage and handovers all land after it, so a snapshot taken
        // here would be stale by the time the player reads the report. The reader takes the live
        // value instead, which keeps the report and the region dossier from disagreeing.
        public double SharedLevelBefore { get; }

        public double AmountBuilt => LevelAfter - LevelBefore;

        public ConstructionProgressReport(
            DefenseType constructionType,
            RegionFaction regionFaction,
            IReadOnlyList<string> squadNames,
            bool isPlayerConstruction,
            double levelBefore,
            double levelAfter,
            double sharedLevelBefore)
        {
            ConstructionType = constructionType;
            RegionFaction = regionFaction;
            SquadNames = squadNames ?? new List<string>();
            IsPlayerConstruction = isPlayerConstruction;
            LevelBefore = levelBefore;
            LevelAfter = levelAfter;
            SharedLevelBefore = sharedLevelBefore;
        }
    }
}
