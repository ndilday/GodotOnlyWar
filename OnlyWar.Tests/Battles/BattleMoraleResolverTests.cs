using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Helpers.Database.GameRules;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Phase 3 resolver wiring for OnlyWar_TDD.md §6.6: the morale check runs at
/// resolver stage 6, a rout sets the existing WithdrawalRole.Routing seam, emits
/// SquadRouted, stays sticky, and flows into BattleSideIntent.Rout / BattleEndReason.Rout
/// through the existing withdrawal machinery. Consumes the seeded static RNG.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class BattleMoraleResolverTests
{
    // Rules-database ids for the battle-ready NPC forces this test fights. Non-player
    // templates carry their own MOS training, weapon set, armour and BattleValue, so squads
    // built from them behave like real combatants. Hand-rolled player-side templates
    // (TestModelFactory.MarineTemplate) do NOT: they have empty MosTraining (nobody can shoot)
    // and a nominal BattleValue, which silently breaks the morale force-context math.
    private const int TyranidFactionId = 2;
    // Termagaunt Squad: 10-30 Devourer gaunts in 5mm chitin, BattleValue 6, Ego 8 — the exact
    // gaunt case MoraleConstants' hand-calculation is written around, and leaderless (no
    // sergeant), so it gets no LeaderThresholdBonus.
    private const int TermagauntSquadTemplateId = 18;
    private const long TermagauntBroodBudget = 60L; // 10 gaunts
    // PDF Infantry Squad: a fixed 1 sergeant + 9 autogun troopers, BattleValue 5, Ego 10.
    private const int PdfInfantrySquadTemplateId = 34;
    // Four platoons (40 men, 200 BV) against the 60 BV brood: enough massed autogun fire to
    // inflict real casualties, and enough of a BattleValue/headcount gap that the gaunts read as
    // decisively losing rather than mauling one isolated platoon into breaking first.
    private const int PdfPlatoonCount = 4;

    [Fact]
    public void LowEgoSquadUnderSustainedFire_RoutsAndBattleEndsInRout()
    {
        // A leaderless Termagaunt brood (10 gaunts, 60 BV) advances across open ground into
        // massed autogun fire from four PDF platoons (40 men, 200 BV). The gaunts are both
        // outgunned in BattleValue and locally outnumbered 4:1, and they take the casualties, so
        // the §5.2 context multiplier climbs and morale must break them (Routing, SquadRouted
        // event) rather than let them close and swarm the line. The PDF's Avoid aggression then
        // declines pursuit, so the routed side disengages and the battle records the typed Rout
        // outcome.
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        Faction tyranids = blob.Factions.Single(faction => faction.Id == TyranidFactionId);
        Faction imperial = blob.Factions.Single(faction => faction.IsDefaultFaction);

        // Seeded separately from the battle: CreateResolver re-seeds for the fight itself, so
        // soldier generation and combat are both deterministic.
        RNG.Reset(74_050);
        BattleSquad victims = CreateNpcSquad(
            tyranids, TermagauntSquadTemplateId, "Termagaunt Brood", TermagauntBroodBudget);
        List<BattleSquad> platoons = Enumerable.Range(0, PdfPlatoonCount)
            .Select(i => CreateNpcSquad(
                imperial, PdfInfantrySquadTemplateId, $"PDF Platoon {i + 1}"))
            .ToList();

        BattleGridManager grid = new();
        for (int i = 0; i < victims.Soldiers.Count; i++)
        {
            Place(grid, victims.Soldiers[i], side: true, x: 60 + (i % 5), y: i / 5);
        }
        for (int i = 0; i < platoons.Count; i++)
        {
            PlaceLine(grid, platoons[i], y: i);
        }

        BattleTurnResolver resolver = CreateResolver(
            grid,
            [victims],
            platoons,
            attackerAggression: Aggression.Aggressive,
            defenderAggression: Aggression.Avoid);
        bool completed = false;
        resolver.OnBattleComplete += (_, _) => completed = true;
        for (int turn = 0; turn < 1000 && !completed; turn++)
        {
            resolver.ProcessNextTurn();
        }

        Assert.True(completed);
        BattleOutcome outcome = Assert.IsType<BattleOutcome>(resolver.BattleHistory.Outcome);
        List<BattleEvent> events = resolver.BattleHistory.Turns
            .SelectMany(turn => turn.Events)
            .ToList();
        BattleEvent routed = events.FirstOrDefault(e => e.Type == BattleEventType.SquadRouted);
        Assert.True(routed != null,
            $"end={outcome.EndReason} turns={resolver.BattleHistory.Turns.Count} "
            + $"events=[{string.Join("; ", events.Select(e => $"{e.TurnNumber}:{e.Type}:{e.PrimarySquadId}"))}] "
            + $"victimsAble={victims.AbleSoldiers.Count} "
            + $"pdfAble={platoons.Sum(p => p.AbleSoldiers.Count)}");
        Assert.Equal(victims.Id, routed.PrimarySquadId);
        Assert.Contains(victims.Id, outcome.RoutingSquadIds);
        Assert.Equal(BattleEndReason.Rout, outcome.EndReason);
        Assert.Equal(BattleSide.Opposing, outcome.SideHoldingField);

        // Rout is sticky (§6): after the SquadRouted turn, every later snapshot in which
        // the squad is still active shows it Routing — it never reverts to a fighting role.
        foreach (BattleTurn turn in resolver.BattleHistory.Turns
            .Where(t => t.TurnNumber > routed.TurnNumber))
        {
            if (turn.State.AttackerSquads.TryGetValue(victims.Id, out BattleSquadSnapshot snapshot)
                && snapshot.Status == BattleSquadStatus.Active)
            {
                Assert.Equal(WithdrawalRole.Routing, snapshot.WithdrawalRole);
            }
        }
    }

    [Fact]
    public void HighEgoSquadsFightingToAnnihilation_NeverRout()
    {
        // Evenly matched high-Ego (14) squads grind each other down until one is wiped out:
        // §2 requires that ordinary attrition never breaks marine-grade morale, so neither side
        // may emit SquadRouted even as the loser is annihilated (§2, §10 Phase 7).
        //
        // These squads are built from the hand-rolled player-side MarineTemplate, which has no
        // MosTraining — so nobody can shoot and the casualties all come from melee. That is
        // fine for this test (the assertion is about morale under casualties, whatever inflicts
        // them) but it is why this case is NOT named for exchanging fire: it provides no ranged
        // attrition coverage. See CreateNpcSquad above for the battle-ready alternative.
        BattleSquad first = CreateSquad(
            "First Marines", 74_301, TestModelFactory.MarineTemplate, count: 5, ego: 14f);
        BattleSquad second = CreateSquad(
            "Second Marines", 74_401, TestModelFactory.MarineTemplate, count: 5, ego: 14f);
        BattleGridManager grid = new();
        for (int i = 0; i < 5; i++)
        {
            Place(grid, first.Soldiers[i], side: true, x: i, y: 0);
            Place(grid, second.Soldiers[i], side: false, x: i, y: 30);
        }

        BattleTurnResolver resolver = CreateResolver(
            grid,
            [first],
            [second],
            attackerAggression: Aggression.Aggressive,
            defenderAggression: Aggression.Aggressive);
        bool completed = false;
        resolver.OnBattleComplete += (_, _) => completed = true;
        for (int turn = 0; turn < 1000 && !completed; turn++)
        {
            resolver.ProcessNextTurn();
        }

        Assert.True(completed);
        Assert.DoesNotContain(
            resolver.BattleHistory.Turns.SelectMany(turn => turn.Events),
            e => e.Type == BattleEventType.SquadRouted);
        Assert.Empty(resolver.BattleHistory.Outcome.RoutingSquadIds);
    }

    /// <summary>
    /// Builds a battle-ready squad the way a non-player force is actually raised — through
    /// SquadFactory, which fills the squad template's elements with soldiers carrying their
    /// MosTraining, and BattleSquad's constructor, which allocates the template's default weapon
    /// set and armour. No hand-grafted skills, weapons or BattleValue.
    /// </summary>
    private static BattleSquad CreateNpcSquad(
        Faction faction,
        int squadTemplateId,
        string name,
        long? battleValueBudget = null)
    {
        SquadTemplate squadTemplate = faction.SquadTemplates[squadTemplateId];
        Squad squad = battleValueBudget.HasValue
            ? SquadFactory.GenerateSquadWithinBudget(
                squadTemplate, battleValueBudget.Value, StaticRNG.Instance, name)
            : SquadFactory.GenerateSquad(squadTemplate, StaticRNG.Instance, name);
        return new BattleSquad(false, squad);
    }

    private static void PlaceLine(BattleGridManager grid, BattleSquad squad, int y)
    {
        for (int i = 0; i < squad.Soldiers.Count; i++)
        {
            Place(grid, squad.Soldiers[i], side: false, x: i, y: y);
        }
    }

    private static BattleTurnResolver CreateResolver(
        BattleGridManager grid,
        IList<BattleSquad> attackers,
        IList<BattleSquad> defenders,
        Aggression attackerAggression,
        Aggression defenderAggression)
    {
        GameRulesData rules = new();
        Date date = new(1, 1, 1);
        string originalDirectory = Environment.CurrentDirectory;
        try
        {
            Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
            GameDataSingleton.Instance.LoadGameDataFromBlob(rules, date, null);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }

        RNG.Reset(74_000);
        BattleAftermathDependencies aftermath = new(
            date,
            StaticRNG.Instance,
            NoOpPlayerBattleAftermathSink.Instance);
        BattleExecutionContext execution = new(rules, StaticRNG.Instance, aftermath);
        return new BattleTurnResolver(
            grid,
            attackers,
            defenders,
            region: null,
            execution,
            new BattleSideProfile(attackerAggression, BattleRole.Attacker),
            new BattleSideProfile(defenderAggression, BattleRole.Defender));
    }

    private static BattleSquad CreateSquad(
        string name,
        int firstSoldierId,
        SoldierTemplate soldierTemplate,
        int count,
        float ego,
        float dexterity = 10f)
    {
        Faction faction = CreateFaction(firstSoldierId + 10_000, name, soldierTemplate);
        SquadTemplate squadTemplate = new(
            firstSoldierId,
            $"{name} Template",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(soldierTemplate, 0, (byte)count)],
            SquadTypes.None)
        {
            Faction = faction
        };
        Squad squad = new(name, null, squadTemplate);
        for (int i = 0; i < count; i++)
        {
            Soldier soldier = TestModelFactory.CreateSoldier(soldierTemplate, $"{name} {i + 1}");
            soldier.Id = firstSoldierId + i;
            soldier.Ego = ego;
            soldier.Dexterity = dexterity;
            squad.AddSquadMember(soldier);
        }
        return new BattleSquad(false, squad);
    }

    private static void Place(
        BattleGridManager grid,
        BattleSoldier soldier,
        bool side,
        int x,
        int y)
    {
        soldier.TopLeft = new ValueTuple<int, int>(x, y);
        grid.PlaceSoldier(soldier, side, [soldier.TopLeft.Value]);
    }

    private static Faction CreateFaction(int id, string name, SoldierTemplate template)
    {
        return new Faction(
            id,
            name,
            Color.Red,
            isPlayerFaction: false,
            isDefaultFaction: false,
            behavior: FactionBehavior.None,
            GrowthType.None,
            new Dictionary<int, Species> { [template.Species.Id] = template.Species },
            new Dictionary<int, SoldierTemplate> { [template.Id] = template },
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, Models.Units.UnitTemplate>(),
            new Dictionary<int, Models.Fleets.BoatTemplate>(),
            new Dictionary<int, Models.Fleets.ShipTemplate>(),
            new Dictionary<int, Models.Fleets.FleetTemplate>());
    }

    private sealed class NoOpPlayerBattleAftermathSink : IPlayerBattleAftermathSink
    {
        public static NoOpPlayerBattleAftermathSink Instance { get; } = new();

        public void MoveToFallenBrothers(PlayerSoldier soldier) { }
        public void AddRecoveredGeneseed(float purity) { }
        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) { }
    }
}
