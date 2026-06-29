using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
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

    // Regression (Design/OpeningScenario.md §8 step 7 note / §12): a fully generated sector must
    // run forward through ProcessTurn without crashing once an order produces a combat/recon
    // mission with assigned squads. Order creation (FactionStrategyController for NPCs, a directly
    // constructed Order here) never set Squad.CurrentOrders, so the first turn such a mission ran,
    // the infiltrate step's BattleSquad.ShouldContinueMission dereferenced a null CurrentOrders and
    // threw a NullReferenceException. The compact SectorSimulationFixture never exercises real
    // battles, which is why this slipped through; this test runs a real stamped sector forward with
    // a genuine combat mission landing on the BattleSquad path every turn.
    [Fact]
    public void ProcessTurn_RealSectorRunForward_RunsCombatWithoutCrashing()
    {
        RNG.Reset(20250628);
        Sector sector = SectorBuilder.GenerateSector(7, _data, _date, "Crusade Chapter");
        // Register the generated sector so turn processing can resolve GameDataSingleton.Instance.Sector.
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);

        // A stamped Tyranid region is the standing combat target for the run.
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);
        RegionFaction tyranidTarget = promised.Regions
            .Select(r => r.RegionFactionMap.TryGetValue(Tyranids.Id, out RegionFaction rf) ? rf : null)
            .First(rf => rf != null);

        // A mission context with assigned squads is produced only by ProcessCombatMissions, i.e.
        // the BattleSquad path that hosts the regression. Seeing one each turn proves real battles
        // ran (not just population/growth bookkeeping) and that the run never threw.
        bool sawCombatWithSquads = false;
        for (int turn = 0; turn < TurnsToSimulate; turn++)
        {
            // Each turn, send a fresh, still-manned squad to recon the Tyranid region. The squad is
            // embarked (no CurrentRegion), so the orchestrator routes it through InfiltrateMissionStep
            // — the exact step whose ShouldContinue hit the null CurrentOrders. Building the Order
            // directly (without manually assigning CurrentOrders) is what reproduces the bug, so this
            // guards the fix in the Order constructor. A new squad each turn keeps the order off any
            // squad that prior combat has emptied (constructing a BattleSquad from an unmanned squad
            // is a separate edge, out of scope here).
            Squad strikeSquad = sector.PlayerForce.Army.OrderOfBattle.GetAllSquads()
                .FirstOrDefault(s => s.CurrentOrders == null && s.Members.Any(m => m.CanFight));
            Order reconOrder = null;
            if (strikeSquad != null)
            {
                Mission reconMission = new Mission(MissionType.Recon, tyranidTarget, 0);
                reconOrder = new Order(new List<Squad> { strikeSquad }, Disposition.Mobile,
                                       isQuiet: true, isActivelyEngaging: false, Aggression.Cautious, reconMission);
                sector.AddNewOrder(reconOrder);
            }

            TurnController controller = new();
            controller.ProcessTurn(sector);
            if (controller.MissionContexts.Any(c => c.Order.AssignedSquads.Any()))
            {
                sawCombatWithSquads = true;
            }

            // Retire this turn's order so it never re-runs against a now-depleted squad next turn.
            if (reconOrder != null)
            {
                sector.RemoveOrder(reconOrder);
                strikeSquad.CurrentOrders = null;
            }
        }

        Assert.True(sawCombatWithSquads,
            "expected a combat/recon mission with assigned squads to run so the BattleSquad "
            + "turn-processing path is actually exercised");
    }

    // Regression (Design/OpeningScenario.md §8/§12): a combat Order persists across turns —
    // ProcessTurn never clears orders — and dead soldiers are permanently removed from
    // Squad.Members (BattleTurnResolver.RemoveSoldiersKilledInBattle). So once combat wipes or
    // fully incapacitates an order's squad, the next turn TurnController.ProcessCombatMissions would
    // re-construct a BattleSquad from the now-unmanned squad, and BattleSquad.AllocateEquipment threw
    // ArgumentOutOfRangeException ("tempSquad[0]") because it assumed AbleSoldiers was non-empty.
    // This blocked long headless forward-sim / balance-tuning runs. The fix skips depleted squads in
    // ProcessCombatMissions (and guards AllocateEquipment). This test plants a persistent combat
    // order on a squad, depletes that squad each turn (the "members remain but none can fight" state
    // combat leaves behind), and runs the real sector forward asserting no crash.
    [Fact]
    public void ProcessTurn_PersistentOrderOnDepletedSquad_DoesNotCrash()
    {
        RNG.Reset(20250628);
        Sector sector = SectorBuilder.GenerateSector(11, _data, _date, "Attrition Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);

        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);
        RegionFaction tyranidTarget = promised.Regions
            .Select(r => r.RegionFactionMap.TryGetValue(Tyranids.Id, out RegionFaction rf) ? rf : null)
            .First(rf => rf != null);

        // Plant a single persistent combat order: it is NOT removed between turns, so it re-runs
        // against the same squad every turn — exactly the path that crashed once the squad emptied.
        Squad strikeSquad = sector.PlayerForce.Army.OrderOfBattle.GetAllSquads()
            .First(s => s.Members.Any(m => m.CanFight));
        Mission reconMission = new Mission(MissionType.Recon, tyranidTarget, 0);
        Order persistentOrder = new Order(new List<Squad> { strikeSquad }, Disposition.Mobile,
                                          isQuiet: true, isActivelyEngaging: false, Aggression.Cautious, reconMission);
        sector.AddNewOrder(persistentOrder);

        for (int turn = 0; turn < TurnsToSimulate; turn++)
        {
            // Re-incapacitate every member each turn (the weekly healing pass would otherwise restore
            // them), so when ProcessCombatMissions runs the order it always finds an unmanned squad.
            DepleteSquad(strikeSquad);
            Assert.NotEmpty(strikeSquad.Members);
            Assert.DoesNotContain(strikeSquad.Members, m => m.CanFight);

            // Before the fix this threw on the first turn the depleted squad reached the BattleSquad
            // path; the assertion is simply that the full run completes without an exception.
            new TurnController().ProcessTurn(sector);
        }

        // The order survives the run (depleted squads are skipped, not disbanded), and the squad is
        // still depleted — proving the persistent order kept landing on the unmanned-squad path.
        Assert.Contains(persistentOrder.Id, sector.Orders.Keys);
        Assert.DoesNotContain(strikeSquad.Members, m => m.CanFight);
    }

    // Sever a vital hit location on every member so CanFight is false while each soldier remains in
    // Squad.Members — the depleted-but-not-empty state combat leaves behind.
    private static void DepleteSquad(Squad squad)
    {
        foreach (ISoldier member in squad.Members)
        {
            HitLocation vital = member.Body.HitLocations.First(hl => hl.Template.IsVital);
            vital.Wounds = new Wounds(vital.Template.SeverWound, 0);
        }
    }

    private const int TurnsToSimulate = 20;

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
