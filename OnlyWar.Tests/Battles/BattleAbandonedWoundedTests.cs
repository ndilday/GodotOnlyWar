using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Helpers.Database.GameRules;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
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
    // Phase 3 of Design/Reference/EngagementScoringOverhaul.md, which removed the arrival-time
    // discount from ranged removal; before that, the squad-level rout heading and the
    // FindBestLocation sidestep fix). Keep the force size explicit and raise it when the PDF stops
    // holding the field: this is an aftermath test, not a balance baseline, and it only needs a
    // side that reliably ends up standing on the bodies.
    private const int PdfPlatoonCount = 6;

    [Fact]
    public void BattleEnd_SideHoldingFieldFinishesOffTheWoundedTheLoserLeftBehind()
    {
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        Faction imperial = blob.Factions.Single(faction => faction.IsDefaultFaction);
        Faction tyranids = blob.Factions.Single(faction => faction.Id == TyranidFactionId);

        // Seeded separately from the battle, which CreateResolver re-seeds, so both soldier
        // generation and the fight itself are deterministic.
        RNG.Reset(75_050);
        List<BattleSquad> platoons = Enumerable.Range(0, PdfPlatoonCount)
            .Select(i => CreateNpcSquad(imperial, PdfInfantrySquadTemplateId, $"PDF Platoon {i + 1}"))
            .ToList();
        BattleSquad brood = CreateNpcSquad(
            tyranids, TermagauntSquadTemplateId, "Termagaunt Brood", TermagauntBroodBudget);
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
            defenderAggression: Aggression.Aggressive);
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
        // A wound that never crippled a vital location did not kill anyone by itself, so a gaunt
        // in that state was maimed and left, not shot dead. At least one must exist, or the
        // assertion above would hold with or without the coup de grâce and prove nothing.
        Dictionary<int, ISoldier> bodies = SoldiersByIdAcrossBattle(history);
        Assert.Contains(leftOnTheField, id => IsMaimedButNotMortallyWounded(bodies[id]));

        // The holding side recovers its own wounded: a PDF trooper who went down without a
        // mortal wound is a casualty, not a death, and must stay out of the tally.
        HashSet<int> pdfStillStanding = finalState.AttackerSquads.Values
            .SelectMany(squad => squad.Soldiers)
            .Select(soldier => soldier.Id)
            .ToHashSet();
        Assert.All(
            pdfStartingIds.Where(id =>
                !pdfStillStanding.Contains(id) && IsMaimedButNotMortallyWounded(bodies[id])),
            id => Assert.DoesNotContain(id, history.KilledSoldierIds));
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
        long? battleValueBudget = null)
    {
        SquadTemplate squadTemplate = faction.SquadTemplates[squadTemplateId];
        Squad squad = battleValueBudget.HasValue
            ? SquadFactory.GenerateSquadWithinBudget(
                squadTemplate, battleValueBudget.Value, StaticRNG.Instance, name)
            : SquadFactory.GenerateSquad(squadTemplate, StaticRNG.Instance, name);
        return new BattleSquad(false, squad);
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

        RNG.Reset(75_000);
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
