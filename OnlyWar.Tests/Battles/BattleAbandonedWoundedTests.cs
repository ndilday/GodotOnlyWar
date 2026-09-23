using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using OnlyWar.Generation.World;
using OnlyWar.Domain;
using OnlyWar.Battles;
using OnlyWar.Battles.Aftermath;
using OnlyWar.Persistence.Database.GameRules;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// A soldier can be taken out of a fight without being killed — a severed leg, a ruined weapon
/// hand — and the wound resolver removes him from the field without adding him to
/// BattleHistory.KilledSoldierIds. The side left standing on that ground finishes off the enemy
/// wounded it finds there, so those soldiers must be counted dead once the battle ends
/// (BattleTurnResolver.FinishOffAbandonedWounded). Without it the debrief undercounts: a battle
/// that removed all 19 cultists from the field reported 15 dead.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class BattleAbandonedWoundedTests
{
    // Battle-ready non-player forces, as in BattleMoraleResolverTests: NPC templates carry their
    // own MOS training, weapons, armour and BattleValue, so the fight produces real wounds.
    private const int TyranidFactionId = 2;
    private const int TermagauntSquadTemplateId = 18;
    private const long TermagauntBroodBudget = 90L; // 15 gaunts — enough casualties that some
                                                    // go down maimed rather than dead.
    private const int PdfInfantrySquadTemplateId = 34;
    // Engagement scoring and movement changes shift this seeded battle's winner (most recently
    // Phase 3 of Design/Reference/BattleLogic.md, which removed the arrival-time
    // discount from ranged removal; before that, the squad-level rout heading and the
    // FindBestLocation sidestep fix). Keep the force size explicit and raise it when the PDF stops
    // holding the field: this is an aftermath test, not a balance baseline, and it only needs a
    // side that reliably ends up standing on the bodies.
    private const int PdfPlatoonCount = 6;

    // The anti-vacuity check below needs a battle that leaves at least one gaunt maimed but not
    // mortally wounded, and whether a given seed does so moves whenever targeting or aim timing
    // changes the random stream. It was hand re-seeded twice (75_000 -> 75_001, then broken again
    // by the 2026-09-22 aim/ammunition changes); now the test takes the first of a few seeds that
    // produces one. Every seed must still satisfy the real assertions.
    private static readonly int[] BattleSeeds = [75_001, 75_002, 75_003, 75_004, 75_005];

    [Fact]
    public void BattleEnd_SideHoldingFieldFinishesOffTheWoundedTheLoserLeftBehind()
    {
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        Faction imperial = blob.Factions.Single(faction => faction.IsDefaultFaction);
        Faction tyranids = blob.Factions.Single(faction => faction.Id == TyranidFactionId);

        foreach (int battleSeed in BattleSeeds)
        {
            if (RunAndCheck(imperial, tyranids, battleSeed))
            {
                return;
            }
        }
        Assert.Fail(
            "no battle seed left a maimed-but-not-mortally-wounded gaunt, so the coup-de-grace "
                + $"assertion would prove nothing; tried {string.Join(", ", BattleSeeds)}");
    }

    // Runs one seeded battle, asserts the aftermath rules, and reports whether the battle was a
    // meaningful test of them (at least one gaunt was maimed rather than killed outright).
    private static bool RunAndCheck(Faction imperial, Faction tyranids, int battleSeed)
    {
        // Seeded separately from the battle, which CreateResolver re-seeds, so both soldier
        // generation and the fight itself are deterministic.
        RNG.Reset(75_050);
        OnlyWar.Abstractions.IEntityIdAllocator entityIds =
            new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
        List<BattleSquad> platoons = Enumerable.Range(0, PdfPlatoonCount)
            .Select(i => CreateNpcSquad(
                imperial,
                PdfInfantrySquadTemplateId,
                $"PDF Platoon {i + 1}",
                entityIds: entityIds))
            .ToList();
        BattleSquad brood = CreateNpcSquad(
            tyranids,
            TermagauntSquadTemplateId,
            "Termagaunt Brood",
            TermagauntBroodBudget,
            entityIds);
        // The live rosters lose their casualties as the battle runs, so record who started.
        List<int> broodStartingIds = brood.Soldiers.Select(s => s.Soldier.Id).ToList();
        List<int> pdfStartingIds = platoons
            .SelectMany(platoon => platoon.Soldiers)
            .Select(s => s.Soldier.Id)
            .ToList();

        BattleGridManager grid = new();
        for (int i = 0; i < platoons.Count; i++)
        {
            PlaceLine(grid, platoons[i], side: true, y: i);
        }
        for (int i = 0; i < brood.Soldiers.Count; i++)
        {
            Place(grid, brood.Soldiers[i], side: false, x: 60 + (i % 5), y: i / 5);
        }

        BattleTurnResolver resolver = CreateResolver(
            grid,
            platoons,
            [brood],
            attackerAggression: Aggression.Aggressive,
            defenderAggression: Aggression.Aggressive,
            battleSeed);
        bool completed = false;
        resolver.OnBattleComplete += (_, _) => completed = true;
        for (int turn = 0; turn < 1000 && !completed; turn++)
        {
            resolver.ProcessNextTurn();
        }

        Assert.True(completed);
        BattleHistory history = resolver.BattleHistory;
        BattleOutcome outcome = Assert.IsType<BattleOutcome>(history.Outcome);
        Assert.Equal(BattleSide.Attacker, outcome.SideHoldingField);

        // Everyone the brood no longer has on its roster went down on ground the PDF ended up
        // holding — whether the PDF was wiped out or the survivors broke and left them there.
        BattleStateSnapshot finalState = history.Turns[^1].State;
        HashSet<int> stillStanding = finalState.OpposingSquads.Values
            .SelectMany(squad => squad.Soldiers)
            .Select(soldier => soldier.Id)
            .ToHashSet();
        List<int> leftOnTheField = broodStartingIds
            .Where(id => !stillStanding.Contains(id))
            .ToList();

        Assert.NotEmpty(leftOnTheField);
        Assert.All(leftOnTheField, id => Assert.Contains(id, history.KilledSoldierIds));
        // The holding side recovers its own wounded: a PDF trooper who went down without a
        // mortal wound is a casualty, not a death, and must stay out of the tally.
        Dictionary<int, ISoldier> bodies = SoldiersByIdAcrossBattle(history);
        HashSet<int> pdfStillStanding = finalState.AttackerSquads.Values
            .SelectMany(squad => squad.Soldiers)
            .Select(soldier => soldier.Id)
            .ToHashSet();
        Assert.All(
            pdfStartingIds.Where(id =>
                !pdfStillStanding.Contains(id) && IsMaimedButNotMortallyWounded(bodies[id])),
            id => Assert.DoesNotContain(id, history.KilledSoldierIds));

        // A wound that never crippled a vital location did not kill anyone by itself, so a gaunt
        // in that state was maimed and left, not shot dead. At least one must exist, or the
        // assertion above would hold with or without the coup de grâce and prove nothing.
        return leftOnTheField.Any(id => IsMaimedButNotMortallyWounded(bodies[id]));
    }

    // Soldiers removed as casualties drop out of the live state, but every turn snapshot retains
    // that turn's casualties, and each snapshot holds the live soldier — so the union across turns
    // reaches everyone who started, with their end-of-battle wounds.
    private static Dictionary<int, ISoldier> SoldiersByIdAcrossBattle(BattleHistory history) =>
        history.Turns
            .SelectMany(turn => turn.State.Soldiers.Values)
            .GroupBy(soldier => soldier.Id)
            .ToDictionary(group => group.Key, group => group.First().Soldier);

    // The wound resolver only calls a soldier dead when a vital location is crippled. Anything
    // short of that — a severed leg, a ruined weapon hand — takes him out of the fight alive.
    private static bool IsMaimedButNotMortallyWounded(ISoldier soldier) =>
        !soldier.Body.HitLocations.Any(location => location.Template.IsVital && location.IsCrippled);

    private static BattleSquad CreateNpcSquad(
        Faction faction,
        int squadTemplateId,
        string name,
        long? battleValueBudget = null,
        OnlyWar.Abstractions.IEntityIdAllocator entityIds = null)
    {
        SquadTemplate squadTemplate = faction.SquadTemplates[squadTemplateId];
        Squad squad = battleValueBudget.HasValue
            ? SquadFactory.GenerateSquadWithinBudget(
                squadTemplate, battleValueBudget.Value, new StaticRNG(), entityIds, name)
            : SquadFactory.GenerateSquad(squadTemplate, new StaticRNG(), entityIds, name);
        return new BattleSquad(false, squad);
    }

    private static BattleTurnResolver CreateResolver(
        BattleGridManager grid,
        IList<BattleSquad> attackers,
        IList<BattleSquad> defenders,
        Aggression attackerAggression,
        Aggression defenderAggression,
        int battleSeed)
    {
        GameRulesData rules = OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(OnlyWar.Tests.Fixtures.RulesDatabaseFixture.DatabasePath);
        Date date = new(1, 1, 1);
        string originalDirectory = Environment.CurrentDirectory;
        try
        {
            Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }

        RNG.Reset(battleSeed);
        StaticRNG random = new();
        BattleAftermathDependencies aftermath = new(
            date,
            random,
            NoOpPlayerBattleAftermathSink.Instance);
        BattleExecutionContext execution = new(rules, random, aftermath);
        return new BattleTurnResolver(
            grid,
            attackers,
            defenders,
            region: null,
            execution,
            new BattleSideProfile(attackerAggression, BattleRole.Attacker),
            new BattleSideProfile(defenderAggression, BattleRole.Defender));
    }

    private static void PlaceLine(BattleGridManager grid, BattleSquad squad, bool side, int y)
    {
        for (int i = 0; i < squad.Soldiers.Count; i++)
        {
            Place(grid, squad.Soldiers[i], side, x: i, y: y);
        }
    }

    private static void Place(BattleGridManager grid, BattleSoldier soldier, bool side, int x, int y)
    {
        soldier.TopLeft = new ValueTuple<int, int>(x, y);
        grid.PlaceSoldier(soldier, side, [soldier.TopLeft.Value]);
    }

    private sealed class NoOpPlayerBattleAftermathSink : IPlayerBattleAftermathSink
    {
        public static NoOpPlayerBattleAftermathSink Instance { get; } = new();

        public void MoveToFallenBrothers(PlayerSoldier soldier) { }
        public void AddRecoveredGeneseed(float purity) { }
        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) { }
    }
}
