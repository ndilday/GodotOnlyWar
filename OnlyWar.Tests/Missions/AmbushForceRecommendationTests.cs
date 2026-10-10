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

// "Recommended force: N squads" measures an ambush target in the chapter's full-strength reference
// squad. It is a general statement for the player, not keyed to the squads in the region. These cover
// the counting rule itself; the screen-level wiring is in PlanetaryOperationsServiceTests.
public class AmbushForceRecommendationTests
{
    private const long ReferenceSquad = 100;

    [Theory]
    [InlineData(1L, 1)]
    [InlineData(100L, 1)]
    [InlineData(101L, 2)]
    [InlineData(250L, 3)]
    [InlineData(0L, 1)]
    public void RecommendSquads_MeasuresTheTargetInReferenceSquads(
        long target, long expectedSquads)
    {
        Assert.Equal(expectedSquads, AmbushMissionSizing.RecommendSquads(target, ReferenceSquad));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void RecommendSquads_RequiresAPositiveReferenceSquad(long reference)
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => AmbushMissionSizing.RecommendSquads(100, reference));
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

        // ...and a target is measured against exactly that.
        Assert.Equal(
            3,
            AmbushMissionSizing.RecommendSquads(
                expected * 2 + 1, AmbushMissionSizing.FullStrengthBattleValue(tactical)));
    }

    // --- the wording: how precise the line may be depends on the intel level alone ---

    [Theory]
    [InlineData(7f, 150L, "Recommended force: 2 squads")]
    [InlineData(4f, 150L, "Recommended force: 2–3 squads")]
    [InlineData(1.5f, 150L, "Recommended force: roughly 1–4 squads")]
    [InlineData(0.5f, 150L, "Recommended force: strength too uncertain; recon advised")]
    [InlineData(7f, 250L, "Recommended force: 3 squads")]
    [InlineData(4f, 250L, "Recommended force: 3–4 squads")]
    [InlineData(1.5f, 250L, "Recommended force: roughly 1–6 squads")]
    public void FormatRecommendedForce_KeepsTheIntelLevelWording(
        float evidence, long target, string expected)
    {
        Mission mission = CreateMission(target, evidence, imperialTarget: false);

        Assert.Equal(
            expected,
            SpecialMissionPresentation.FormatRecommendedForce(mission, 0, ReferenceSquad));
    }

    // An Imperial target is always known well enough to name a number, whatever the intel says.
    [Theory]
    [InlineData(150L, "Recommended force: 2 squads")]
    [InlineData(250L, "Recommended force: 3 squads")]
    public void FormatRecommendedForce_ImperialTargetIsNeverTooUncertain(
        long target, string expected)
    {
        Mission mission = CreateMission(target, evidence: 0f, imperialTarget: true);

        Assert.Equal(
            expected,
            SpecialMissionPresentation.FormatRecommendedForce(mission, 0, ReferenceSquad));
    }

    [Fact]
    public void FormatRecommendedForce_OneSquadIsNamedInTheSingular()
    {
        Mission mission = CreateMission(100, evidence: 7f, imperialTarget: false);

        Assert.Equal(
            "Recommended force: 1 squad",
            SpecialMissionPresentation.FormatRecommendedForce(mission, 0, ReferenceSquad));
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
