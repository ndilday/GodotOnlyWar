using OnlyWar.Helpers.UI;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.UI;

public class SquadRowViewModelTests
{
    [Fact]
    public void StrengthSnapshot_UsesTemplateFullStrengthAndNonOverlappingUnavailableReasons()
    {
        Squad squad = new("Line", null, TestModelFactory.SquadTemplate);
        PlayerSoldier healthy = new(TestModelFactory.CreateSoldier(), "Healthy");
        PlayerSoldier wounded = new(TestModelFactory.CreateSoldier(), "Wounded");
        PlayerSoldier reserved = new(TestModelFactory.CreateSoldier(), "Reserved")
        {
            IsUndergoingMedicalProcedure = true
        };
        wounded.Body.HitLocations.First(location => location.Template.IsVital && !location.Template.IsMotive)
            .Wounds = new Wounds(
                wounded.Body.HitLocations.First(location => location.Template.IsVital && !location.Template.IsMotive)
                    .Template.CrippleWound,
                0);
        squad.AddSquadMember(healthy);
        squad.AddSquadMember(wounded);
        squad.AddSquadMember(reserved);

        SquadStrengthSnapshot snapshot = SquadStrengthSnapshotBuilder.Build(squad);

        Assert.Equal(5, snapshot.Full);
        Assert.Equal(3, snapshot.Rostered);
        Assert.Equal(3, snapshot.Present);
        Assert.Equal(1, snapshot.Effective);
        Assert.Equal(2, snapshot.Unavailable);
        Assert.Equal(2, snapshot.Vacancies);
        Assert.Equal(1, snapshot.InjuryOrIncapacitationCount);
        Assert.Equal(1, snapshot.ProcedureReservationCount);
        Assert.Equal(2, snapshot.InjuryOrIncapacitationCount + snapshot.ProcedureReservationCount);
    }

    [Fact]
    public void StrengthSnapshot_FullNeverFallsBelowAnOverstrengthRoster()
    {
        Squad squad = new("Overstrength", null, TestModelFactory.SquadTemplate);
        for (int i = 0; i < 7; i++)
        {
            squad.AddSquadMember(TestModelFactory.CreateSoldier(name: $"Marine {i}"));
        }

        SquadStrengthSnapshot snapshot = SquadStrengthSnapshotBuilder.Build(squad);

        Assert.Equal(7, snapshot.Full);
        Assert.Equal(7, snapshot.Rostered);
        Assert.Equal(0, snapshot.Vacancies);
    }

    [Fact]
    public void Readiness_DistinguishesVacantLeaderFromUnavailableLeader()
    {
        SquadTemplate template = new(
            901,
            "Led Formation",
            TestModelFactory.DefaultWeapons,
            new List<SquadWeaponOption>(),
            TestModelFactory.TestArmor,
            [
                new SquadTemplateElement(TestModelFactory.SergeantTemplate, 0, 1),
                new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 4)
            ],
            SquadTypes.None);

        Squad vacant = new("Vacant", null, template);
        vacant.AddSquadMember(TestModelFactory.CreateSoldier(name: "Marine"));
        SquadReadinessSnapshot vacantReadiness = SquadReadinessService.Evaluate(
            vacant,
            new SquadRowContext(SquadRowContextKind.PlanetaryOperations, SquadRowAction.BeginOrder));

        Squad led = new("Led", null, template);
        PlayerSoldier leader = new(
            TestModelFactory.CreateSoldier(TestModelFactory.SergeantTemplate), "Sergeant");
        led.AddSquadMember(leader);
        led.AddSquadMember(TestModelFactory.CreateSoldier(name: "Marine"));
        SquadReadinessSnapshot ready = SquadReadinessService.Evaluate(led);
        leader.Body.HitLocations.First(location => location.Template.IsVital && !location.Template.IsMotive)
            .Wounds = new Wounds(
                leader.Body.HitLocations.First(location => location.Template.IsVital && !location.Template.IsMotive)
                    .Template.CrippleWound,
                0);
        SquadReadinessSnapshot leaderOut = SquadReadinessService.Evaluate(led);

        Assert.Equal(SquadLeaderStatus.Vacant, vacantReadiness.LeaderStatus);
        Assert.Equal(SquadReadinessBlocker.Leaderless, vacantReadiness.PrimaryBlocker);
        Assert.False(vacantReadiness.CanBeginDeployment);
        Assert.Equal(SquadLeaderStatus.Ready, ready.LeaderStatus);
        Assert.Equal(SquadLeaderStatus.Unavailable, leaderOut.LeaderStatus);
        Assert.True(leaderOut.CanBeginDeployment);
    }

    [Fact]
    public void RowBuilder_UsesSameSnapshotAndAddsProjectedDeltaModel()
    {
        Squad squad = new("Projected", null, TestModelFactory.SquadTemplate);
        squad.AddSquadMember(TestModelFactory.CreateSoldier(name: "Marine 1"));
        SquadRowViewModel source = new SquadRowViewModelBuilder().Build(
            squad,
            new SquadRowContext(
                SquadRowContextKind.Muster,
                SquadRowAction.Inspect,
                contextBadge: "MUSTER"));

        ProjectedSquadRowViewModel projected = new SquadRowViewModelBuilder().BuildProjected(
            source, outgoingDelta: 1, incomingDelta: 2, futureStrength: 2, "squad:1");

        Assert.Equal(source.Strength, projected.Strength);
        Assert.Equal("1/5", source.StrengthLabel);
        Assert.Equal(1, projected.OutgoingDelta);
        Assert.Equal(2, projected.IncomingDelta);
        Assert.Equal(2, projected.FutureStrength);
        Assert.Equal("squad:1", projected.ProvisionalKey);
    }
}
