using OnlyWar.Battles.Abstractions;
using OnlyWar.Battles;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Missions;

public class EngagementStateReuseTests
{
    [Fact]
    public void MultiDayMission_ReusesAdapterStateWhileRefreshingParticipants()
    {
        BattleEngagementResolver adapter =
            TestExecutionContextFactory.CreateEngagementResolver();
        Squad campaignSquad = TestModelFactory.CreateSquad(
            "Multi-day force",
            TestModelFactory.CreateSoldier(name: "First participant"),
            TestModelFactory.CreateSoldier(name: "Second participant"));
        OperationalMissionElement element = adapter.CreateSquad(
            isPlayerSquad: true,
            campaignSquad);
        IEngagementState state = element.State;
        BattleEngagementResolver.BattleSquadEngagementState tacticalState =
            Assert.IsType<BattleEngagementResolver.BattleSquadEngagementState>(state);
        BattleSquad retainedBattleSquad = tacticalState.BattleSquad;
        ISoldier first = element.Members[0];
        ISoldier second = element.Members[1];

        // Day 1: the full duty-ready set enters the engagement.
        element.RefreshEngagementParticipants([first, second]);
        adapter.Update(element);
        Assert.Equal(2, retainedBattleSquad.AbleSoldiers.Count);

        // Day 2: Operations changes eligibility, but the adapter updates the same tactical state.
        element.RefreshEngagementParticipants([second]);
        adapter.Update(element);
        Assert.Same(state, element.State);
        Assert.Same(retainedBattleSquad, tacticalState.BattleSquad);
        Assert.Single(retainedBattleSquad.AbleSoldiers);
        Assert.Equal(second.Id, retainedBattleSquad.AbleSoldiers[0].Soldier.Id);

        // Day 3: a participant may be admitted again without reconstructing equipment or state.
        element.RefreshEngagementParticipants([first, second]);
        adapter.Update(element);
        Assert.Same(state, element.ToEngagementParticipant().State);
        Assert.Equal(2, retainedBattleSquad.AbleSoldiers.Count);
    }
}
