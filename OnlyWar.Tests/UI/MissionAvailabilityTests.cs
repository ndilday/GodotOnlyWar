using System.Linq;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.UI;

public class MissionAvailabilityTests
{
    [Fact]
    public void CurrentRegion_PublicEnemiesProduceOneFactionSpecificAttackEach()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction cult = fixture.AddPublicCult(
            region: 0, population: 2_000, organization: 100);
        RegionFaction tyranids = fixture.AddConsumptionFaction(
            region: 0, population: 3_000, organization: 100);
        fixture.AddHiddenFaction(
            region: 0, GrowthType.Conversion, population: 1_000);
        Region region = fixture.Planet.Regions[0];

        AvailableMission[] attacks = MissionAvailability
            .GetAvailableMissions(region, region)
            .Where(mission => mission.Kind == MissionAvailabilityKind.Attack)
            .ToArray();

        Assert.Equal(2, attacks.Length);
        Assert.Contains(attacks, mission =>
            mission.Label == "Attack (Genestealer Cult)"
            && ReferenceEquals(mission.TargetFaction, cult));
        Assert.Contains(attacks, mission =>
            mission.Label == "Attack (Tyranids)"
            && ReferenceEquals(mission.TargetFaction, tyranids));
        Assert.Equal(2, attacks.Select(mission => mission.IdentityKey).Distinct().Count());
    }

    [Fact]
    public void FactionSpecificAttackRepresentsOnlyOrdersAgainstItsTarget()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction cult = fixture.AddPublicCult(
            region: 0, population: 2_000, organization: 100);
        RegionFaction tyranids = fixture.AddConsumptionFaction(
            region: 0, population: 3_000, organization: 100);
        Region region = fixture.Planet.Regions[0];
        AvailableMission cultAttack = MissionAvailability
            .GetAvailableMissions(region, region)
            .Single(mission => mission.TargetFaction == cult);
        Order cultOrder = new([], true, true, Aggression.Normal,
            new Mission(MissionType.Advance, cult, 0));
        Order tyranidOrder = new([], true, true, Aggression.Normal,
            new Mission(MissionType.Advance, tyranids, 0));

        Assert.True(cultAttack.RepresentsOrder(cultOrder));
        Assert.False(cultAttack.RepresentsOrder(tyranidOrder));
        Assert.Equal("Attack (Genestealer Cult)",
            MissionAvailability.GetOrderLabel(cultOrder.Mission));
    }

    [Theory]
    [InlineData(DefenseType.Entrenchment, "Build Fortifications")]
    [InlineData(DefenseType.ListeningPost, "Build Listening Post")]
    [InlineData(DefenseType.AntiAir, "Build Anti-Air")]
    public void ConstructionLabelsMatchOrderButtons(DefenseType type, string expectedLabel)
    {
        Assert.Equal(expectedLabel, MissionAvailability.GetConstructionLabel(type));
    }

    [Theory]
    [InlineData(DefenseType.Entrenchment, "Build Fortifications")]
    [InlineData(DefenseType.ListeningPost, "Build Listening Post")]
    [InlineData(DefenseType.AntiAir, "Build Anti-Air")]
    public void ConstructionOrderLabelsNameTheSpecificType(DefenseType type, string expectedLabel)
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        ConstructionMission mission = new(type, 0, fixture.DefaultRegionFaction(0));

        Assert.Equal(expectedLabel, MissionAvailability.GetOrderLabel(mission));
    }

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

    [Theory]
    [InlineData(90L, "Recommended Minimum Force: 1 squad")]
    [InlineData(91L, "Recommended Minimum Force: 2 squads")]
    [InlineData(270L, "Recommended Minimum Force: 3 squads")]
    public void AmbushLabelsKeepRecommendedForceOutOfCompactButtonText(
        long targetBattleValue,
        string expected)
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction target = fixture.AddPublicCult(
            region: 0, population: 2_000, organization: 100);
        Mission mission = new(
            MissionType.Ambush,
            target,
            missionSize: 1,
            targetBattleValue);
        target.Region.SpecialMissions.Add(mission);

        AvailableMission available = MissionAvailability
            .GetAvailableMissions(target.Region, target.Region)
            .Single(option => option.SpecialMission?.Id == mission.Id);

        Assert.DoesNotContain(expected, available.Label);
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
