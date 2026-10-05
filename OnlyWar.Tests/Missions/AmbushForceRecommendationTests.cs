using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Operations.Missions;
using OnlyWar.Operations.Missions.Ambush;
using OnlyWar.Persistence.Database.GameRules;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Missions;

// "Recommended force: N squads" answers how many of the player's own squads must be committed to
// reach parity with an ambush target. These cover the counting rule itself; the screen-level wiring
// (which squads count as available) is in PlanetaryOperationsServiceTests.
public class AmbushForceRecommendationTests
{
    private static AmbushForceAvailability Available(params long[] squadBattleValues) =>
        new(squadBattleValues, ReferenceSquadBattleValue: 100);

    // Listed in the order the roster happens to hold them: taking them in that order would need
    // three squads (10 + 100 + 60), taking the strongest first needs two (100 + 60).
    [Fact]
    public void RecommendSquads_TakesTheStrongestAvailableSquadsFirst()
    {
        SquadRecommendation recommendation = AmbushMissionSizing.RecommendSquads(
            150, Available(10, 100, 60, 30));

        Assert.Equal(2, recommendation.Squads);
        Assert.False(recommendation.ExceedsAvailable);
        Assert.True(recommendation.BasedOnAvailableSquads);
    }

    [Fact]
    public void RecommendSquads_ParityIsReachedExactlyWhenTheCommittedValueEqualsTheTarget()
    {
        Assert.Equal(2, AmbushMissionSizing.RecommendSquads(160, Available(100, 60)).Squads);
        SquadRecommendation oneShort = AmbushMissionSizing.RecommendSquads(
            161, Available(100, 60));
        Assert.True(oneShort.ExceedsAvailable);
    }

    // The player cannot field more squads than he has, so the answer is a shortfall, not a count
    // of squads that do not exist.
    [Fact]
    public void RecommendSquads_WhenAllAvailableSquadsFallShort_ReportsTheShortfallNotAnInventedCount()
    {
        SquadRecommendation recommendation = AmbushMissionSizing.RecommendSquads(
            1_000, Available(50, 40));

        Assert.True(recommendation.ExceedsAvailable);
        Assert.Equal(2, recommendation.Squads);
        Assert.True(recommendation.BasedOnAvailableSquads);
    }

    [Fact]
    public void RecommendSquads_IgnoresSquadsWithNoBattleValue()
    {
        SquadRecommendation recommendation = AmbushMissionSizing.RecommendSquads(
            10, Available(0, 0, 10));

        Assert.Equal(1, recommendation.Squads);
        Assert.False(recommendation.ExceedsAvailable);
    }

    [Theory]
    [InlineData(1L, 1)]
    [InlineData(100L, 1)]
    [InlineData(101L, 2)]
    [InlineData(250L, 3)]
    [InlineData(0L, 1)]
    public void RecommendSquads_WithNothingAvailable_MeasuresTheTargetInReferenceSquads(
        long target, long expectedSquads)
    {
        SquadRecommendation recommendation = AmbushMissionSizing.RecommendSquads(
            target, Available());

        Assert.Equal(expectedSquads, recommendation.Squads);
        Assert.False(recommendation.ExceedsAvailable);
        Assert.False(recommendation.BasedOnAvailableSquads);
    }

    // The reference squad is rules data, so it must follow the database through any future
    // recalibration: the expectation is recomputed from the loaded template's own elements.
    [Fact]
    public void ReferenceSquad_IsTheRulesTacticalSquadAtFullStrength()
    {
        GameRulesData rules = GameRulesLoader.Load(RulesDatabaseFixture.DatabasePath);
        SquadTemplate tactical = rules.ChapterDoctrine.TacticalSquad;

        long expected = tactical.Elements.Sum(
            element => (long)element.MaximumNumber * element.SoldierTemplate.BattleValue);

        Assert.True(expected > 0);
        Assert.Equal(expected, AmbushMissionSizing.FullStrengthBattleValue(tactical));

        // ...and an empty roster is measured against exactly that.
        SquadRecommendation recommendation = AmbushMissionSizing.RecommendSquads(
            expected * 2 + 1,
            new AmbushForceAvailability([], AmbushMissionSizing.FullStrengthBattleValue(tactical)));
        Assert.Equal(3, recommendation.Squads);
    }

    [Fact]
    public void SquadBattleValue_SumsTheMembersTemplateBattleValues()
    {
        Squad squad = TestModelFactory.CreateSquad(
            "Valued Squad",
            TestModelFactory.CreateSoldier(TestModelFactory.SergeantTemplate, "Sergeant"),
            TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate, "Marine A"),
            TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate, "Marine B"));

        Assert.Equal(
            TestModelFactory.SergeantTemplate.BattleValue
            + 2 * TestModelFactory.MarineTemplate.BattleValue,
            AmbushMissionSizing.SquadBattleValue(squad));
    }

    [Fact]
    public void SquadBattleValue_OfNoSquad_IsZero()
    {
        Assert.Equal(0, AmbushMissionSizing.SquadBattleValue(null));
    }

    // --- the wording: how precise the line may be depends on the intel level alone ---

    [Theory]
    [InlineData(7f, 150L, "Recommended force: 2 squads")]
    [InlineData(4f, 150L, "Recommended force: 2–3 squads")]
    [InlineData(1.5f, 150L, "Recommended force: roughly 1–4 squads")]
    [InlineData(0.5f, 150L, "Recommended force: strength too uncertain; recon advised")]
    [InlineData(7f, 161L, "Recommended force: more than your 2 available squads")]
    [InlineData(4f, 161L, "Recommended force: more than your 2 available squads")]
    [InlineData(1.5f, 161L, "Recommended force: more than your 2 available squads")]
    [InlineData(0.5f, 161L, "Recommended force: strength too uncertain; recon advised")]
    public void FormatRecommendedForce_KeepsTheIntelLevelWording(
        float evidence, long target, string expected)
    {
        Mission mission = CreateMission(target, evidence, imperialTarget: false);

        Assert.Equal(
            expected,
            SpecialMissionPresentation.FormatRecommendedForce(mission, 0, Available(100, 60)));
    }

    // An Imperial target is always known well enough to name a number, whatever the intel says.
    [Theory]
    [InlineData(150L, "Recommended force: 2 squads")]
    [InlineData(161L, "Recommended force: more than your 2 available squads")]
    public void FormatRecommendedForce_ImperialTargetIsNeverTooUncertain(
        long target, string expected)
    {
        Mission mission = CreateMission(target, evidence: 0f, imperialTarget: true);

        Assert.Equal(
            expected,
            SpecialMissionPresentation.FormatRecommendedForce(mission, 0, Available(100, 60)));
    }

    [Fact]
    public void FormatRecommendedForce_OneAvailableSquadIsNamedInTheSingular()
    {
        Mission mission = CreateMission(500, evidence: 7f, imperialTarget: false);

        Assert.Equal(
            "Recommended force: more than your 1 available squad",
            SpecialMissionPresentation.FormatRecommendedForce(mission, 0, Available(100)));
    }

    // The player is the observer (a default faction), the target a separate hidden-or-public
    // faction in the same region; `evidence` is what the observer knows of it (0 = nothing).
    private static Mission CreateMission(long target, float evidence, bool imperialTarget)
    {
        Planet planet = new(1, "Test Planet", new Coordinate(0, 0), 1, null, 0, 0);
        Region region = new(1, planet, 0, "Target Region", new RegionCoordinate(0, 0), 0);
        planet.Regions[0] = region;
        Faction observerFaction = CreateFaction(10, "Observer", isDefault: true);
        PlanetFaction observer = new(observerFaction);
        planet.PlanetFactionMap[observerFaction.Id] = observer;
        Faction targetFaction = CreateFaction(20, "Target", isDefault: imperialTarget);
        PlanetFaction targetPlanetFaction = new(targetFaction);
        planet.PlanetFactionMap[targetFaction.Id] = targetPlanetFaction;
        RegionFaction targetPresence = new(targetPlanetFaction, region);
        region.RegionFactionMap[targetFaction.Id] = targetPresence;

        if (evidence > 0f)
        {
            observer.SeedTargetBelief(
                region, targetFaction, evidence,
                estimatedPopulation: null, estimatedMilitaryStrength: null, evidenceWeek: 0);
        }
        return new Mission(MissionType.Ambush, targetPresence, missionSize: 1, target);
    }

    private static Faction CreateFaction(int id, string name, bool isDefault) =>
        new(
            id,
            name,
            Color.Red,
            isPlayerFaction: false,
            isDefaultFaction: isDefault,
            behavior: FactionBehavior.PopulationIsMilitary,
            GrowthType.Conversion,
            new Dictionary<int, Species>(),
            new Dictionary<int, SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
}
