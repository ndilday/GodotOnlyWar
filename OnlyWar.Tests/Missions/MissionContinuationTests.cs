using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Missions;

public class MissionContinuationTests
{
    [Fact]
    public void CautiousUnderstrengthSquad_CanStartAtFullMissionStrength()
    {
        BattleSquad battleSquad = CreateOrderedSquad(Aggression.Cautious, memberCount: 3);

        Assert.True(battleSquad.ShouldContinueMission());
    }

    [Fact]
    public void CautiousSquad_AbortsAfterLosingMoreThanQuarterOfStartingForce()
    {
        BattleSquad battleSquad = CreateOrderedSquad(Aggression.Cautious, memberCount: 3);

        battleSquad.RemoveSoldier(battleSquad.Soldiers[0]);

        Assert.False(battleSquad.ShouldContinueMission());
    }

    private static BattleSquad CreateOrderedSquad(Aggression aggression, int memberCount)
    {
        Squad squad = TestModelFactory.CreateSquad("Understrength Squad");
        for (int i = 0; i < memberCount; i++)
        {
            squad.AddSquadMember(TestModelFactory.CreateSoldier(name: $"Scout {i + 1}"));
        }
        _ = new Order(
            [squad],
            Disposition.Raiding,
            isQuiet: true,
            isActivelyEngaging: false,
            levelOfAggression: aggression,
            mission: new Mission(MissionType.Recon, regionFaction: null, missionSize: 0));
        return new BattleSquad(true, squad);
    }
}
