using System;
using System.Linq;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Orders;
using OnlyWar.Helpers.Readiness;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class ReadinessBoundaryTests
{
    [Fact]
    public void SameExplicitInputsAgreeInRowsOrdersAndBattleMaterialization()
    {
        var fixture = SectorSimulationFixture.CreateDetached();
        Squad squad = CreateSquad(fixture.Sector.PlayerForce.Faction);
        PlayerSoldier member = (PlayerSoldier)squad.Members.Last();
        RecruitmentProgram program = new();
        program.Procedures.Add(new RecruitmentProcedure
        {
            SubjectId = member.Id,
            Type = RecruitmentProcedureType.BlackCarapace
        });
        ChapterOperationalDoctrine doctrine = new(null, true, 5);
        Order order = new([], true, false, Aggression.Normal,
            new Mission(700, MissionType.Patrol, fixture.DefaultRegionFaction(0), 1));

        SquadRowViewModel row = new SquadRowViewModelBuilder().Build(squad,
            SquadRowContext.ForNewOrder(), program, doctrine);
        Assert.Equal(4, row.Strength.DutyReady);
        Assert.False(row.Readiness.CanBeginDeployment);
        Assert.False(OrderForceService.AssignSquad(order, squad, doctrine, program));
        Assert.Empty(order.AssignedSquads);
        Assert.Null(squad.CurrentOrders);
        Assert.Empty(BattleSquadFactory.GetParticipants(squad, doctrine, program));

        program.Procedures.Clear();
        Assert.True(new SquadRowViewModelBuilder().Build(squad,
            SquadRowContext.ForNewOrder(), program, doctrine).Readiness.CanBeginDeployment);
        Assert.Equal(5, BattleSquadFactory.GetParticipants(squad, doctrine, program).Count);
        Assert.True(OrderForceService.AssignSquad(order, squad, doctrine, program));
    }

    [Fact]
    public void PolicyDoesNotSelectTheActiveCampaignAndAdapterRejectsUnrelatedFactionInstances()
    {
        GameDataSingleton active = GameDataSingleton.Instance;
        var oldRules = active.GameRulesData;
        var oldSector = active.Sector;
        var oldDate = active.Date;
        bool oldUpgrade = active.UpgradePending;
        try
        {
            var live = SectorSimulationFixture.CreateDetached();
            var detached = SectorSimulationFixture.CreateDetached();
            Squad liveSquad = CreateSquad(live.Sector.PlayerForce.Faction);
            PlayerSoldier liveMember = (PlayerSoldier)liveSquad.Members.Last();
            Squad detachedSquad = CreateSquad(detached.Sector.PlayerForce.Faction, liveMember.Id);
            PlayerSoldier detachedMember = (PlayerSoldier)detachedSquad.Members.Last();
            live.Sector.PlayerForce.RecruitmentProgram = new RecruitmentProgram();
            live.Sector.PlayerForce.RecruitmentProgram.Procedures.Add(new RecruitmentProcedure
            {
                Type = RecruitmentProcedureType.BlackCarapace,
                SubjectId = liveMember.Id
            });
            active.LoadGameDataFromBlob(null, new Date(42, 1, 1), live.Sector);

            Assert.True(DutyReadinessService.Evaluate(liveMember).IsDutyReady);
            Assert.True(DutyReadinessService.Evaluate(detachedMember).IsDutyReady);
            Assert.Null(CurrentCampaignReadinessContext.ResolveProgram(detachedSquad));
            Assert.Null(CurrentCampaignReadinessContext.ResolveDoctrine(detachedSquad));
            Assert.Equal(5, new SquadRowViewModelBuilder().Build(detachedSquad).Strength.DutyReady);
            Assert.Equal(4, new SquadRowViewModelBuilder().Build(liveSquad).Strength.DutyReady);

            active.LoadGameDataFromBlob(null, new Date(42, 1, 2), detached.Sector);
            Assert.Null(CurrentCampaignReadinessContext.ResolveProgram(liveSquad));
            Assert.Equal(5, new SquadRowViewModelBuilder().Build(liveSquad).Strength.DutyReady);
        }
        finally
        {
            active.LoadGameDataFromBlob(oldRules, oldDate, oldSector, oldUpgrade);
        }
    }

    private static Squad CreateSquad(Faction faction, int? lastSoldierId = null)
    {
        SquadTemplate template = new(702, "Test formation", TestModelFactory.DefaultWeapons, [],
            TestModelFactory.TestArmor,
            [new(TestModelFactory.SergeantTemplate, 0, 1), new(TestModelFactory.MarineTemplate, 0, 4)],
            SquadTypes.None) { Faction = faction };
        Squad squad = new("Test formation", null, template);
        for (int index = 0; index < 5; index++)
        {
            Soldier soldier = TestModelFactory.CreateSoldier(
                index == 0 ? TestModelFactory.SergeantTemplate : TestModelFactory.MarineTemplate);
            if (index == 4 && lastSoldierId.HasValue) soldier.Id = lastSoldierId.Value;
            squad.AddSquadMember(new PlayerSoldier(soldier, $"Brother {index}"));
        }
        return squad;
    }
}
