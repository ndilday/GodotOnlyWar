using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models.Missions;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Missions;

public class MissionForcePolicyTests
{
    [Fact]
    public void Recon_ResolvesAsOneIndependentElementPerSquad()
    {
        BattleSquad first = CreateBattleSquad("First");
        BattleSquad second = CreateBattleSquad("Second");

        var elements = MissionTurnProcessor.BuildMissionElements(
            MissionType.Recon,
            [first, second]);

        Assert.Equal(MissionForceMode.IndependentSquads,
            MissionForcePolicy.GetMode(MissionType.Recon));
        Assert.Equal(2, elements.Count);
        Assert.Same(first, Assert.Single(elements[0]));
        Assert.Same(second, Assert.Single(elements[1]));
    }

    [Theory]
    [InlineData(MissionType.LightningRaid)]
    [InlineData(MissionType.Assassination)]
    [InlineData(MissionType.Sabotage)]
    [InlineData(MissionType.Advance)]
    public void MassForceMissions_ResolveAsOneUnifiedElement(MissionType missionType)
    {
        BattleSquad first = CreateBattleSquad("First");
        BattleSquad second = CreateBattleSquad("Second");

        var elements = MissionTurnProcessor.BuildMissionElements(
            missionType,
            [first, second]);

        Assert.Equal(MissionForceMode.UnifiedForce, MissionForcePolicy.GetMode(missionType));
        Assert.Single(elements);
        Assert.Equal([first, second], elements[0]);
    }

    private static BattleSquad CreateBattleSquad(string name) =>
        new(true, TestModelFactory.CreateSquad(
            name,
            TestModelFactory.CreateSoldier(name: $"{name} Scout")));
}
