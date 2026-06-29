using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

// Turn-loop integration for the "Promised World" opening (Design/OpeningScenario.md §6, step 5):
// the GrowthMultiplier throttle applied in EndOfTurnRegionFactionsUpdate and the ProcessScenario
// win/lapse resolution. Throttle is exercised through the compact SectorSimulationFixture; the
// win/lapse paths are exercised against a real stamped sector so they read the actual scenario,
// Sector Lord, and promised-world state.
public class ScenarioTurnTests
{
    private readonly GameRulesData _data;
    private readonly Date _date = new(39, 500, 1);

    public ScenarioTurnTests()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        _data = new GameRulesData();
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, null);
    }

    private Faction Tyranids => _data.SectorFactions.Invader;
    private Faction Imperial => _data.DefaultFaction;

    // §6.1 — a throttled region grows strictly slower than an identical unthrottled one.
    [Fact]
    public void GrowthThrottle_ThrottledRegionGrowsSlowerThanIdenticalUnthrottled()
    {
        RNG.Reset(1);
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        // Two identical logistic populations in separate, uncapped regions; only the multiplier
        // differs. 20000 keeps both below the revolt threshold (a revolting cult would flip public
        // and break the one-public-faction-per-region invariant), and the unthrottled growth
        // (20000 * 0.0006 = 12) is whole, so its per-turn change is rounding-free; the throttled
        // region's ~60% reduction keeps it strictly behind over the run.
        RegionFaction unthrottled = fixture.AddHiddenFaction(0, GrowthType.Logistic, population: 20000);
        RegionFaction throttled = fixture.AddHiddenFaction(1, GrowthType.Logistic, population: 20000);
        throttled.GrowthMultiplier = ScenarioRules.TyranidGrowthMultiplier;

        for (int turn = 0; turn < 10; turn++)
        {
            fixture.ProcessTurn();
        }

        Assert.True(unthrottled.Population > 20000, "unthrottled region should have grown");
        Assert.True(throttled.Population < unthrottled.Population,
            $"throttled ({throttled.Population}) should grow slower than unthrottled ({unthrottled.Population})");
    }

    // The default multiplier (1.0) leaves growth identical to the pre-throttle behavior, so every
    // non-scenario region is unaffected.
    [Fact]
    public void GrowthThrottle_DefaultMultiplierLeavesGrowthUnchanged()
    {
        RNG.Reset(1);
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction cult = fixture.AddHiddenFaction(0, GrowthType.Logistic, population: 20000);
        Assert.Equal(1.0f, cult.GrowthMultiplier);

        fixture.ProcessTurn();

        Assert.Equal(20012, cult.Population); // 20000 * 0.0006 = 12, exactly as before the throttle
    }

    // §6.2 win — no Tyranid presence left: the world is granted to the player and the current
    // Sector Lord's opinion rises.
    [Fact]
    public void ProcessScenario_Win_GrantsWorldAndRaisesSectorLordOpinion()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Victory Chapter");
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);
        Faction player = sector.PlayerForce.Faction;
        Character lord = sector.GetSectorLord();
        Assert.NotNull(lord);
        float opinionBefore = lord.OpinionOfPlayerForce;

        // Liberate the world: clear every Tyranid presence from the promised planet.
        foreach (Region region in promised.Regions)
        {
            region.RegionFactionMap.Remove(Tyranids.Id);
        }

        new TurnController().ProcessScenario(sector);

        Assert.Equal(ObjectiveState.Won, sector.Scenario.State);
        // The player is installed as the planet-wide controlling faction (the Chapter World).
        Assert.True(promised.PlanetFactionMap.ContainsKey(player.Id));
        Assert.Equal(1, promised.PlanetFactionMap[player.Id].PlayerReputation);
        Assert.All(promised.Regions, r => Assert.True(r.RegionFactionMap.ContainsKey(player.Id)));
        Assert.Equal(player, promised.GetControllingFaction());
        // The current Sector Lord's opinion rises (resolved at resolution time).
        Assert.Equal(opinionBefore + ScenarioRules.SectorLordOpinionReward,
                     sector.GetSectorLord().OpinionOfPlayerForce, precision: 4);
    }

    // §6.2 lapse — the world is fully overrun (no Imperial and no player presence): the promise is
    // withdrawn (no world granted) and the current Sector Lord's opinion falls.
    [Fact]
    public void ProcessScenario_Lapse_WithdrawsAndLowersSectorLordOpinion()
    {
        Sector sector = SectorBuilder.GenerateSector(2, _data, _date, "Lost Chapter");
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);
        Faction player = sector.PlayerForce.Faction;
        Character lord = sector.GetSectorLord();
        Assert.NotNull(lord);
        float opinionBefore = lord.OpinionOfPlayerForce;

        // Overrun the world: strip every Imperial and player presence, leaving only the Tyranids.
        foreach (Region region in promised.Regions)
        {
            region.RegionFactionMap.Remove(Imperial.Id);
            region.RegionFactionMap.Remove(player.Id);
        }
        Assert.Contains(promised.Regions, r => r.RegionFactionMap.ContainsKey(Tyranids.Id));

        new TurnController().ProcessScenario(sector);

        Assert.Equal(ObjectiveState.Lapsed, sector.Scenario.State);
        // No Chapter World granted.
        Assert.False(promised.PlanetFactionMap.ContainsKey(player.Id));
        // The current Sector Lord's opinion falls.
        Assert.Equal(opinionBefore - ScenarioRules.SectorLordOpinionPenalty,
                     sector.GetSectorLord().OpinionOfPlayerForce, precision: 4);
    }

    // §6.2 lapse with a vacant seat (the sector capital itself fell): the reputation move is a
    // no-op but the lapse still resolves.
    [Fact]
    public void ProcessScenario_Lapse_WithVacantSeat_StillResolves()
    {
        Sector sector = SectorBuilder.GenerateSector(2, _data, _date, "Headless Chapter");
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);
        Faction player = sector.PlayerForce.Faction;

        // Vacate the Sector Lord's seat: clear the capital governor so GetSectorLord() is null.
        Planet capital = sector.GetSectorCapital();
        capital.PlanetFactionMap[capital.GetControllingFaction().Id].Leader = null;
        Assert.Null(sector.GetSectorLord());

        foreach (Region region in promised.Regions)
        {
            region.RegionFactionMap.Remove(Imperial.Id);
            region.RegionFactionMap.Remove(player.Id);
        }

        new TurnController().ProcessScenario(sector);

        Assert.Equal(ObjectiveState.Lapsed, sector.Scenario.State);
    }

    // §6.2 — neither outcome fires once the scenario has already resolved (State != Pending).
    [Fact]
    public void ProcessScenario_DoesNothingWhenNotPending()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Settled Chapter");
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);
        Faction player = sector.PlayerForce.Faction;
        sector.Scenario.State = ObjectiveState.Won; // already resolved earlier

        // Even a winnable board (no Tyranids) must not re-grant or re-notify.
        foreach (Region region in promised.Regions)
        {
            region.RegionFactionMap.Remove(Tyranids.Id);
        }

        TurnController controller = new();
        controller.ProcessScenario(sector);

        Assert.Equal(ObjectiveState.Won, sector.Scenario.State);
        Assert.False(promised.PlanetFactionMap.ContainsKey(player.Id));
        Assert.Null(controller.ScenarioNotification);
    }

    // The win path surfaces a notification; ProcessTurn clears any stale notification each turn.
    [Fact]
    public void ProcessScenario_Win_SurfacesNotification()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Notified Chapter");
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);
        foreach (Region region in promised.Regions)
        {
            region.RegionFactionMap.Remove(Tyranids.Id);
        }

        TurnController controller = new();
        controller.ProcessScenario(sector);

        Assert.False(string.IsNullOrEmpty(controller.ScenarioNotification));
        Assert.Contains(promised.Name, controller.ScenarioNotification);
    }
}
