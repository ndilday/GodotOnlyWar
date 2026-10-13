using System.Collections.Generic;
using OnlyWar.Campaign.Turns;
using OnlyWar.Domain;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Generation.Abstractions;
using OnlyWar.Medical.Treatment;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

/// <summary>
/// Repair hours come out of a Techmarine's weekly drill in proportion, as mission days do, so the
/// same hour is not credited as both (OnlyWar_TDD.md §6.6.1).
/// </summary>
public sealed class CyberneticRepairDrillTests
{
    [Fact]
    public void RepairHours_ReduceOnlyTheTechmarinesDrill_InProportion()
    {
        Soldier techmarineSource = TestModelFactory.CreateSoldier(name: "Ferrus");
        Soldier brotherSource = TestModelFactory.CreateSoldier(name: "Varro");
        PlayerSoldier techmarine = new(techmarineSource, "Ferrus");
        PlayerSoldier brother = new(brotherSource, "Varro");
        Unit chapter = new("Chapter", new UnitTemplate(100, "Chapter", true, [], []));
        Squad squad = new("Armory", chapter, TestModelFactory.SquadTemplate);
        chapter.AddSquad(squad);
        squad.AddSquadMember(techmarine);
        squad.AddSquadMember(brother);
        PlayerForce force = new(null, new Army("Army", null, "Commander", chapter, [techmarine, brother]),
            new Fleet("Fleet", null, null));
        GameSession session = new(SectorSimulationFixture.Create().Rules,
            new Sector(force, [], [], []), new Date(20_000), new SeededRNG(3));
        RecordingTrainingService training = new();

        new ChapterUpkeepProcessor(session, training).TrainNonDeployedPlayerForces(
            session.Sector,
            repairHoursBySoldier: new Dictionary<int, float> { [techmarine.Id] = 28f });

        // Half a training week of repair leaves half the week's drill.
        Assert.Equal(0.2f, training.Points[brother.Id], 5);
        Assert.Equal(0.2f - 28f * CyberneticRepairConstants.DrillPointsPerRepairHour,
            training.Points[techmarine.Id], 5);
        Assert.Equal(0.1f, training.Points[techmarine.Id], 5);
    }

    private sealed class RecordingTrainingService : ISoldierTrainingService
    {
        public Dictionary<int, float> Points { get; } = [];

        public void ApplySoldierWorkExperience(ISoldier soldier, Squad squad, float points) =>
            Points[soldier.Id] = points;

        public void UpdateRatings(Date date, PlayerSoldier soldier) { }
        public void EvaluateSoldier(PlayerSoldier soldier, Date trainingFinishedYear) { }
        public void TrainScouts(
            IEnumerable<Squad> scoutSquads,
            Dictionary<int, string> squadTrainingOptionMap,
            float points = 0.2f,
            IReadOnlyDictionary<int, float> pointsBySquad = null) { }
    }
}
