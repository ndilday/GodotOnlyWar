using OnlyWar.Campaign;
using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Medical.Readiness;
using OnlyWar.Operations.Orders;
using OnlyWar.Tests.Fixtures;
using System;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Domain;

/// <summary>
/// Mars pipeline (TDD §6.14): the departure operation and who may
/// be sent. Templates come from the real rules data, so the Techmarine requirement (Tech > 60)
/// and the Scout exclusion are tested against what the game actually uses.
/// </summary>
public class MechanicusDepartureTests
{
    private static readonly Date Departure = new(42, 5, 10);

    private readonly SectorSimulationFixture _fixture = SectorSimulationFixture.Create();
    private ChapterGenerationDoctrine Doctrine => _fixture.Rules.ChapterDoctrine;

    [Fact]
    public void Depart_SendsALineBrotherToMarsAsATechmarine()
    {
        Squad line = LineSquad(out PlayerSoldier brother, out PlayerSoldier squadmate);
        Squad armory = Armory();

        IndividualPosting posting = Departures().Depart(brother, armory, Departure);

        Assert.DoesNotContain(brother, line.Members);
        Assert.Contains(squadmate, line.Members);
        Assert.Same(armory, brother.AssignedSquad);
        Assert.Same(Doctrine.Techmarine, brother.Template);
        Assert.Same(posting, brother.IndividualPosting);
        Assert.Equal(IndividualPostingPurpose.Mechanicus, posting.Purpose);
        Assert.True(posting.Location.IsOffSector);
        Assert.Equal(Departure, posting.StartedDate);
        Assert.Equal(
            Departure.GetTotalWeeks() + MechanicusDepartureService.TrainingWeeks,
            posting.ExpectedReturnDate.GetTotalWeeks());
        Assert.Equal(new Date(42, 25, 10), posting.ExpectedReturnDate);
        Assert.Contains(brother.SoldierEvents, entry => entry.Detail.Contains("Mars"));
        Assert.True(MechanicusTrainingService.IsOnMars(brother));
        Assert.Equal(DutyReadinessReasonCode.OffSector, DutyReadinessService.Evaluate(brother).ReasonCode);
    }

    [Theory]
    [InlineData(ChapterSoldierRole.TacticalMarine)]
    [InlineData(ChapterSoldierRole.AssaultMarine)]
    [InlineData(ChapterSoldierRole.DevastatorMarine)]
    [InlineData(ChapterSoldierRole.Veteran)]
    [InlineData(ChapterSoldierRole.Ancient)]
    [InlineData(ChapterSoldierRole.Champion)]
    public void Evaluate_AllowsEveryLineBrother(ChapterSoldierRole role)
    {
        PlayerSoldier brother = Brother(Doctrine.GetSoldier(role));
        LineSquad(brother);

        Assert.True(SoldierTransferService.IsLineBrother(brother.Template));
        Assert.True(Departures().Evaluate(brother).IsAllowed);
    }

    [Theory]
    [InlineData(ChapterSoldierRole.Sergeant, MechanicusDepartureReasonCode.NotLineBrother)]
    [InlineData(ChapterSoldierRole.VeteranSergeant, MechanicusDepartureReasonCode.NotLineBrother)]
    [InlineData(ChapterSoldierRole.Captain, MechanicusDepartureReasonCode.NotLineBrother)]
    [InlineData(ChapterSoldierRole.Apothecary, MechanicusDepartureReasonCode.NotLineBrother)]
    [InlineData(ChapterSoldierRole.Chaplain, MechanicusDepartureReasonCode.NotLineBrother)]
    [InlineData(ChapterSoldierRole.Techmarine, MechanicusDepartureReasonCode.NotLineBrother)]
    [InlineData(ChapterSoldierRole.ScoutMarine, MechanicusDepartureReasonCode.ScoutMarine)]
    public void Evaluate_RejectsOfficersSpecialistsAndScouts(
        ChapterSoldierRole role, MechanicusDepartureReasonCode expected)
    {
        PlayerSoldier soldier = Brother(Doctrine.GetSoldier(role));
        LineSquad(soldier);

        MechanicusDepartureEvaluation evaluation = Departures().Evaluate(soldier);

        Assert.False(evaluation.IsAllowed);
        Assert.Equal(expected, evaluation.ReasonCode);
        Assert.False(string.IsNullOrWhiteSpace(evaluation.Reason));
    }

    [Fact]
    public void Evaluate_RequiresTechAboveSixty()
    {
        PlayerSoldier atBar = Brother(Doctrine.TacticalMarine, tech: 60f);
        PlayerSoldier aboveBar = Brother(Doctrine.TacticalMarine, tech: 60.5f);
        LineSquad(atBar, aboveBar);

        Assert.Equal(MechanicusDepartureReasonCode.BelowTechmarineRequirement,
            Departures().Evaluate(atBar).ReasonCode);
        Assert.True(Departures().Evaluate(aboveBar).IsAllowed);
    }

    [Fact]
    public void Evaluate_RejectsABrotherWhoseSquadIsCommittedToAnOperation()
    {
        Squad line = LineSquad(out PlayerSoldier brother, out _);
        line.CurrentOrders = DefensiveOrder(line);

        Assert.Equal(MechanicusDepartureReasonCode.CommittedToOperation,
            Departures().Evaluate(brother).ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsAnUnfitBrother()
    {
        LineSquad(out PlayerSoldier brother, out _);
        brother.IsUndergoingMedicalProcedure = true;

        Assert.Equal(MechanicusDepartureReasonCode.NotFit, Departures().Evaluate(brother).ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsABrotherAlreadyOnMars()
    {
        LineSquad(out PlayerSoldier brother, out _);
        Departures().Depart(brother, Armory(), Departure);

        Assert.Equal(MechanicusDepartureReasonCode.AlreadyPosted, Departures().Evaluate(brother).ReasonCode);
    }

    [Fact]
    public void Depart_RefusesAnIneligibleSoldierAndChangesNothing()
    {
        PlayerSoldier sergeant = Brother(Doctrine.Sergeant);
        Squad line = LineSquad(sergeant);
        Squad armory = Armory();

        Assert.Throws<InvalidOperationException>(() =>
            Departures().Depart(sergeant, armory, Departure));

        Assert.Same(line, sergeant.AssignedSquad);
        Assert.Same(Doctrine.Sergeant, sergeant.Template);
        Assert.Null(sergeant.IndividualPosting);
        Assert.Empty(armory.Members);
    }

    // Founders hold no line template when they leave (generation seats them later), and at
    // founding nothing can be unfit or committed: only the Techmarine requirement applies.
    [Fact]
    public void AtFounding_OnlyTheTechmarineRequirementApplies()
    {
        PlayerSoldier founder = Brother(Doctrine.ChapterMaster);
        PlayerSoldier weakFounder = Brother(Doctrine.ChapterMaster, tech: 55f);
        founder.IsUndergoingMedicalProcedure = true;
        Squad armory = Armory();

        Assert.Equal(MechanicusDepartureReasonCode.NotLineBrother, Departures().Evaluate(founder).ReasonCode);
        Assert.True(Departures().Evaluate(founder, atFounding: true).IsAllowed);
        Assert.Equal(MechanicusDepartureReasonCode.BelowTechmarineRequirement,
            Departures().Evaluate(weakFounder, atFounding: true).ReasonCode);

        Departures().Depart(founder, armory, Departure, atFounding: true);

        Assert.Same(armory, founder.AssignedSquad);
        Assert.Same(Doctrine.Techmarine, founder.Template);
        Assert.True(MechanicusTrainingService.IsOnMars(founder));
    }

    [Fact]
    public void Depart_TheLastMemberLeavingDetachesTheEmptySquad()
    {
        PlayerSoldier brother = Brother(Doctrine.TacticalMarine);
        Squad line = LineSquad(brother);
        line.CurrentRegion = _fixture.Planet.Regions[0];

        Departures().Depart(brother, Armory(), Departure);

        Assert.Empty(line.Members);
        Assert.Null(line.CurrentRegion);
    }

    // The design rule: the per-turn awards over the whole period total at least the Techmarine
    // MOS points, skill by skill. Uses the real rules-data profile, so a Techmarine skill missing
    // from it fails here.
    [Fact]
    public void MarsTraining_OverTheFullPeriod_ReachesEveryTechmarineMosSkill()
    {
        LineSquad(out PlayerSoldier brother, out _);
        Departures().Depart(brother, Armory(), Departure);
        var before = Doctrine.Techmarine.MosTraining.ToDictionary(
            mos => mos.Item1.Id, mos => Points(brother, mos.Item1));

        for (int week = 0; week < MechanicusDepartureService.TrainingWeeks; week++)
        {
            MechanicusTrainingService.Train([brother], _fixture.Rules.TrainingProfiles.Values);
        }

        Assert.NotEmpty(Doctrine.Techmarine.MosTraining);
        Assert.All(Doctrine.Techmarine.MosTraining, mos =>
            Assert.True(Points(brother, mos.Item1) - before[mos.Item1.Id] >= mos.Item2,
                $"{mos.Item1.Name}: gained {Points(brother, mos.Item1) - before[mos.Item1.Id]}, "
                + $"MOS needs {mos.Item2}"));
    }

    [Fact]
    public void MarsTraining_TrainsOnlyBrothersOnMars()
    {
        LineSquad(out PlayerSoldier brother, out PlayerSoldier squadmate);
        Departures().Depart(brother, Armory(), Departure);
        BaseSkill servoArm = Doctrine.Techmarine.MosTraining
            .Select(mos => mos.Item1)
            .First(skill => skill.Name == "Servo-Arm");

        MechanicusTrainingService.Train([brother, squadmate], _fixture.Rules.TrainingProfiles.Values);

        Assert.True(Points(brother, servoArm) > 0);
        Assert.Equal(0, Points(squadmate, servoArm));
    }

    private static float Points(ISoldier soldier, BaseSkill skill) =>
        soldier.Skills.SingleOrDefault(s => s.BaseSkill.Id == skill.Id)?.PointsInvested ?? 0;

    private MechanicusDepartureService Departures() =>
        new(new OrderCommitmentSurface(), Doctrine.Techmarine, Doctrine.ScoutMarine);

    private static PlayerSoldier Brother(SoldierTemplate template, float tech = 70f)
    {
        string name = $"Brother {Guid.NewGuid():N}";
        PlayerSoldier soldier = new(TestModelFactory.CreateSoldier(template, name), name);
        soldier.AddEvaluation(new SoldierEvaluation(
            Departure, melee: 50, ranged: 50, lead: 50, med: 50, tech: tech, piety: 50, ancient: 50));
        return soldier;
    }

    private Squad LineSquad(out PlayerSoldier brother, out PlayerSoldier squadmate)
    {
        brother = Brother(Doctrine.TacticalMarine);
        squadmate = Brother(Doctrine.TacticalMarine);
        return LineSquad(brother, squadmate);
    }

    private Squad LineSquad(params PlayerSoldier[] soldiers)
    {
        Unit chapter = new(1, "Chapter", new UnitTemplate(1, "Chapter Template", true, [], []), []);
        Squad squad = new("Tactical Squad", chapter, Doctrine.GetSquad(ChapterSquadRole.TacticalSquad));
        chapter.AddSquad(squad);
        foreach (PlayerSoldier soldier in soldiers) squad.AddSquadMember(soldier);
        return squad;
    }

    private Squad Armory() => new("Armory", null, Doctrine.Armory);

    private Order DefensiveOrder(Squad squad)
    {
        RegionFaction regionFaction = _fixture.Planet.Regions[0].RegionFactionMap.Values.First();
        return new Order([squad], true, false, Aggression.Avoid,
            new Mission(MissionType.DefenseInDepth, regionFaction, 0));
    }
}
