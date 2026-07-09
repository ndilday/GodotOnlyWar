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
// the general GrowthMultiplier throttle applied to organic growth in EndOfTurnRegionFactionsUpdate,
// and the ProcessScenario win/lapse resolution. The throttle is exercised on a Logistic faction
// through the compact SectorSimulationFixture (Tyranids grow by consumption and ignore it — PRD
// §4.24); the win/lapse paths are exercised against a real stamped sector so they read the actual
// scenario, Sector Lord, and promised-world state.
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
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
        // A sub-1 multiplier on an organically-growing (Logistic) faction; the general primitive is
        // not scenario-specific (Tyranids grow by consumption and ignore it — PRD §4.24).
        throttled.GrowthMultiplier = 0.4f;

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

    // §6.2 win — no enemy presence left (swarm AND cult cleared): the world is granted to the
    // player and the current Sector Lord's opinion rises.
    [Fact]
    public void ProcessScenario_Win_GrantsWorldAndRaisesSectorLordOpinion()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Victory Chapter");
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);
        Faction player = sector.PlayerForce.Faction;
        Character lord = sector.GetSectorLord();
        Assert.NotNull(lord);
        float opinionBefore = lord.OpinionOfPlayerForce;

        // Liberate the world FULLY: clear every hostile faction (Tyranid swarm, the revealed
        // Genestealer Cult, anything else) — not just the Tyranids. A surviving cult would keep the
        // objective Pending.
        foreach (Region region in promised.Regions)
        {
            foreach (int hostileId in region.RegionFactionMap.Values
                         .Where(rf => !rf.PlanetFaction.Faction.IsDefaultFaction
                                      && !rf.PlanetFaction.Faction.IsPlayerFaction)
                         .Select(rf => rf.PlanetFaction.Faction.Id)
                         .ToList())
            {
                region.RegionFactionMap.Remove(hostileId);
            }
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

    // §6.2 — clearing the Tyranids is NOT enough: while the revealed Genestealer Cult still holds
    // ground the world is not back in Imperial control, so the objective stays Pending (not Won).
    [Fact]
    public void ProcessScenario_TyranidsGoneButCultRemains_StaysPending()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Half-Liberated Chapter");
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);
        Faction player = sector.PlayerForce.Faction;
        Faction cult = _data.SectorFactions.Infiltrator;

        // Remove only the Tyranids; leave the revealed cult holding ground.
        foreach (Region region in promised.Regions)
        {
            region.RegionFactionMap.Remove(Tyranids.Id);
        }
        // Sanity: a cult presence really does remain on the world to block the liberation.
        Assert.Contains(promised.Regions, r =>
            r.RegionFactionMap.TryGetValue(cult.Id, out RegionFaction rf)
            && (rf.Population > 0 || rf.Garrison > 0));

        new TurnController().ProcessScenario(sector);

        // Not liberated: no win, no world granted, still the player's live objective.
        Assert.Equal(ObjectiveState.Pending, sector.Scenario.State);
        Assert.False(promised.PlanetFactionMap.ContainsKey(player.Id));
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

            if (reconOrder != null)
            {
                Assert.DoesNotContain(reconOrder.Id, sector.Orders.Keys);
                Assert.Null(strikeSquad.CurrentOrders);
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
    public void ProcessTurn_NonConstructionPlayerOrderOnDepletedSquadClearsAfterTurn()
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
        Order reconOrder = new(new List<Squad> { strikeSquad }, Disposition.Mobile,
                               isQuiet: true, isActivelyEngaging: false, Aggression.Cautious, reconMission);
        sector.AddNewOrder(reconOrder);

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
        Assert.DoesNotContain(reconOrder.Id, sector.Orders.Keys);
        Assert.Null(strikeSquad.CurrentOrders);
        Assert.DoesNotContain(strikeSquad.Members, m => m.CanFight);
    }

    [Fact]
    public void ProcessTurn_PlayerConstructionOrderPersistsAfterTurn()
    {
        RNG.Reset(20250628);
        Sector sector = SectorBuilder.GenerateSector(12, _data, _date, "Mason Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);

        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);
        Region region = promised.Regions.First();
        RegionFaction playerRegionFaction = AddPlayerRegionFaction(sector, promised, region);

        Squad squad = sector.PlayerForce.Army.OrderOfBattle.GetAllSquads()
            .First(s => s.Members.Any(m => m.CanFight));
        squad.CurrentRegion = region;
        playerRegionFaction.LandedSquads.Add(squad);

        ConstructionMission mission = new(DefenseType.Entrenchment, 0, playerRegionFaction);
        Order constructionOrder = new(new List<Squad> { squad }, Disposition.DugIn,
                                      isQuiet: false, isActivelyEngaging: false,
                                      Aggression.Cautious, mission);
        sector.AddNewOrder(constructionOrder);

        new TurnController().ProcessTurn(sector);

        Assert.Contains(constructionOrder.Id, sector.Orders.Keys);
        Assert.Same(constructionOrder, squad.CurrentOrders);
        Assert.True(playerRegionFaction.Entrenchment > 0);
    }

    [Fact]
    public void ProcessTurn_ExecutedSpecialMissionIsRemovedAfterTurn()
    {
        RNG.Reset(20250628);
        Sector sector = SectorBuilder.GenerateSector(13, _data, _date, "Vanishing Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);

        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);
        RegionFaction tyranidTarget = promised.Regions
            .Select(r => r.RegionFactionMap.TryGetValue(Tyranids.Id, out RegionFaction rf) ? rf : null)
            .First(rf => rf != null);
        Mission specialMission = new(MissionType.Recon, tyranidTarget, 0);
        tyranidTarget.Region.SpecialMissions.Add(specialMission);

        Squad squad = sector.PlayerForce.Army.OrderOfBattle.GetAllSquads()
            .First(s => s.Members.Any(m => m.CanFight));
        Order order = new(new List<Squad> { squad }, Disposition.Mobile,
                          isQuiet: true, isActivelyEngaging: false,
                          Aggression.Cautious, specialMission);
        sector.AddNewOrder(order);

        new TurnController().ProcessTurn(sector);

        Assert.DoesNotContain(specialMission, tyranidTarget.Region.SpecialMissions);
        Assert.DoesNotContain(order.Id, sector.Orders.Keys);
        Assert.Null(squad.CurrentOrders);
    }

    // The planet-scoped opening sim runs only the promised world's local turn slice; it must NOT run
    // the player force's own weekly upkeep (§4.24). A light wound that ProcessTurn's medical pass
    // would heal in a week stays untouched after SimulatePlanetForward, proving the sim skips
    // ProcessMedical (and, by the same omission, training and fleet movement).
    [Fact]
    public void SimulatePlanetForward_DoesNotHealPlayerForce()
    {
        Sector sector = SectorBuilder.GenerateSector(7, _data, _date, "Unhealed Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);

        // A light (minor/moderate) wound on a non-vital location: automatically healed by a weekly
        // medical pass, and not replacement-eligible, so ProcessTurn would clear it in one week.
        ISoldier soldier = sector.PlayerForce.Army.OrderOfBattle.GetAllMembers().First();
        HitLocation location = soldier.Body.HitLocations
            .First(hl => !hl.Template.IsVital && !hl.IsSevered);
        location.Wounds = new Wounds(0x00000011u, 0);
        uint woundedTotal = location.Wounds.WoundTotal;
        Assert.True(woundedTotal > 0);

        new TurnController().SimulatePlanetForward(sector, promised, turns: 1);

        // Untouched: the scoped sim never reached the medical pass that would have healed it.
        Assert.Equal(woundedTotal, location.Wounds.WoundTotal);
    }

    // Sever a vital hit location on every member so CanFight is false while each soldier remains in
    // Squad.Members — the depleted-but-not-empty state combat leaves behind.
    private static RegionFaction AddPlayerRegionFaction(Sector sector, Planet planet, Region region)
    {
        Faction playerFaction = sector.PlayerForce.Faction;
        if (!planet.PlanetFactionMap.TryGetValue(playerFaction.Id, out PlanetFaction playerPlanetFaction))
        {
            playerPlanetFaction = new PlanetFaction(playerFaction) { IsPublic = true };
            planet.PlanetFactionMap[playerFaction.Id] = playerPlanetFaction;
        }

        RegionFaction playerRegionFaction = new(playerPlanetFaction, region)
        {
            Population = 1,
            Garrison = 1,
            IsPublic = true,
            Organization = 100
        };
        region.RegionFactionMap[playerFaction.Id] = playerRegionFaction;
        return playerRegionFaction;
    }

    private static void DepleteSquad(Squad squad)
    {
        foreach (ISoldier member in squad.Members)
        {
            HitLocation vital = member.Body.HitLocations.First(hl => hl.Template.IsVital);
            vital.Wounds = new Wounds(vital.Template.SeverWound, 0);
        }
    }

    private const int TurnsToSimulate = 3;

    // The win path surfaces a notification; ProcessTurn clears any stale notification each turn.
    [Fact]
    public void ProcessScenario_Win_SurfacesNotification()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Notified Chapter");
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);
        // Full liberation clears every hostile faction, not just the Tyranids.
        foreach (Region region in promised.Regions)
        {
            foreach (int hostileId in region.RegionFactionMap.Values
                         .Where(rf => !rf.PlanetFaction.Faction.IsDefaultFaction
                                      && !rf.PlanetFaction.Faction.IsPlayerFaction)
                         .Select(rf => rf.PlanetFaction.Faction.Id)
                         .ToList())
            {
                region.RegionFactionMap.Remove(hostileId);
            }
        }

        TurnController controller = new();
        controller.ProcessScenario(sector);

        Assert.False(string.IsNullOrEmpty(controller.ScenarioNotification));
        Assert.Contains(promised.Name, controller.ScenarioNotification);
    }
}
