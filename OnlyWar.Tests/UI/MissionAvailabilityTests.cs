using System.Linq;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.UI;

public class MissionAvailabilityTests
{
    [Fact]
    public void SameTypeSpecialMissions_GetStableDistinctLabelsAndIdentity()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction target = fixture.AddPublicCult(
            region: 0, population: 2_000, organization: 100);
        Mission first = new(MissionType.Ambush, target, missionSize: 1);
        Mission second = new(MissionType.Ambush, target, missionSize: 2);
        target.Region.SpecialMissions.Add(first);
        target.Region.SpecialMissions.Add(second);

        AvailableMission[] missions = MissionAvailability
            .GetAvailableMissions(target.Region, target.Region)
            .Where(mission => mission.Kind == MissionAvailabilityKind.Special)
            .ToArray();

        Assert.Equal(2, missions.Length);
        Assert.Contains("Ambush — Genestealer Cult", missions[0].Label);
        Assert.Contains($"Intel M-{first.Id}", missions[0].Label);
        Assert.Contains($"Intel M-{second.Id}", missions[1].Label);
        Assert.NotEqual(missions[0].Label, missions[1].Label);
        Assert.False(missions[0].RepresentsSameOption(missions[1]));
        Assert.True(missions[0].RepresentsSameOption(
            new AvailableMission("Changed display label", MissionAvailabilityKind.Special, first)));
    }

    [Fact]
    public void SabotageLabelsNameKnownObjectiveWithoutRevealingHiddenFaction()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction hiddenTarget = fixture.AddHiddenFaction(
            region: 0, GrowthType.Conversion, population: 2_000);
        SabotageMission mission = new(
            DefenseType.ListeningPost, size: 1, hiddenTarget);
        hiddenTarget.Region.SpecialMissions.Add(mission);

        AvailableMission available = MissionAvailability
            .GetAvailableMissions(hiddenTarget.Region, hiddenTarget.Region)
            .Single(option => option.SpecialMission?.Id == mission.Id);

        Assert.Equal("Sabotage — Unknown cell · Listening Post", available.Label);
        Assert.DoesNotContain(hiddenTarget.PlanetFaction.Faction.Name, available.Label);
    }
}
