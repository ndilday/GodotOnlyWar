using System;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Battles;
using OnlyWar.Battles.Aftermath;
using OnlyWar.Battles.Models;
using OnlyWar.Domain;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Persistence.Database.GameRules;
using OnlyWar.Runtime.Factories;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// A 1x1 enemy has four squares around it. When a man in one of them is cut down, the next
/// charger must be able to take that square in the same turn's closing pass. Observed 2026-09-26
/// (Monody Prime Theta): two lictors against ten scouts, where the dead held their squares until
/// end-of-turn cleanup, so each wave of scouts waited a full turn behind a ring that was already
/// open and was destroyed piecemeal.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class MeleeRingRefillTests
{
    private const int TyranidFactionId = 2;
    private const int LictorSquadTemplateId = 32;
    private const int PdfInfantrySquadTemplateId = 34;

    // The test is only meaningful on a turn where the lictor drops at least one man in its ring.
    // A lictor with seven talon strikes almost always does, but take the first seed that does
    // rather than hand-pinning one that a targeting change can silently break.
    private static readonly int[] BattleSeeds = [91_001, 91_002, 91_003, 91_004, 91_005];

    private static readonly (int X, int Y) LictorCell = (10, 0);

    [Fact]
    public void RingSquareFreedByAKill_IsRefilledInTheSameTurn()
    {
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        Faction imperial = blob.Factions.Single(faction => faction.IsDefaultFaction);
        Faction tyranids = blob.Factions.Single(faction => faction.Id == TyranidFactionId);

        foreach (int battleSeed in BattleSeeds)
        {
            if (RunFirstTurnAndCheck(imperial, tyranids, battleSeed))
            {
                return;
            }
        }
        Assert.Fail(
            "no battle seed had the lictor drop a man in its ring on turn one, so the refill "
                + $"assertion would prove nothing; tried {string.Join(", ", BattleSeeds)}");
    }

    private static bool RunFirstTurnAndCheck(Faction imperial, Faction tyranids, int battleSeed)
    {
        RNG.Reset(91_050);
        OnlyWar.Abstractions.IEntityIdAllocator entityIds =
            new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
        BattleSquad platoon = CreateNpcSquad(
            imperial, PdfInfantrySquadTemplateId, "PDF Platoon", entityIds);
        BattleSquad lictorSquad = CreateNpcSquad(
            tyranids, LictorSquadTemplateId, "Lictor", entityIds);
        BattleSoldier lictor = Assert.Single(lictorSquad.Soldiers);
        Assert.True(platoon.Soldiers.Count > 5, "need men to spare beyond the four-square ring");

        BattleGridManager grid = new();
        Place(grid, lictor, side: false, LictorCell.X, LictorCell.Y);
        (int X, int Y)[] ring =
        [
            (LictorCell.X - 1, LictorCell.Y),
            (LictorCell.X + 1, LictorCell.Y),
            (LictorCell.X, LictorCell.Y + 1),
            (LictorCell.X, LictorCell.Y - 1)
        ];
        List<int> ringIds = [];
        for (int i = 0; i < platoon.Soldiers.Count; i++)
        {
            BattleSoldier trooper = platoon.Soldiers[i];
            if (i < ring.Length)
            {
                Place(grid, trooper, side: true, ring[i].X, ring[i].Y);
                ringIds.Add(trooper.Soldier.Id);
            }
            else
            {
                // A reserve rank three squares back, every man within one move of the ring.
                Place(grid, trooper, side: true, LictorCell.X - 3 + (i - ring.Length), 3);
            }
        }
        // The melee began last turn: both squads open this turn committed to it.
        platoon.IsInMelee = true;
        lictorSquad.IsInMelee = true;

        BattleTurnResolver resolver = CreateResolver(grid, [platoon], [lictorSquad], battleSeed);
        resolver.ProcessNextTurn();

        BattleHistory history = resolver.BattleHistory;
        bool ringManDropped = ringIds.Any(id =>
            history.KilledSoldierIds.Contains(id)
            || history.IncapacitatedSoldierIds.Contains(id));
        if (!ringManDropped || !lictor.IsCombatEffective)
        {
            return false;
        }

        // Cleanup has removed this turn's dead, so every soldier still beside the lictor is alive.
        // Without the same-turn refill each fallen ring man leaves an empty square here.
        Assert.True(
            platoon.AbleSoldiers.Count >= ring.Length,
            "the platoon must still have enough men to fill the ring");
        Assert.Equal(ring.Length, grid.GetAdjacentEnemies(lictor.Soldier.Id).Count);
        return true;
    }

    private static BattleSquad CreateNpcSquad(
        Faction faction,
        int squadTemplateId,
        string name,
        OnlyWar.Abstractions.IEntityIdAllocator entityIds)
    {
        SquadTemplate squadTemplate = faction.SquadTemplates[squadTemplateId];
        Squad squad = SquadFactory.GenerateSquad(squadTemplate, new StaticRNG(), entityIds, name);
        return new BattleSquad(false, squad);
    }

    private static BattleTurnResolver CreateResolver(
        BattleGridManager grid,
        IList<BattleSquad> attackers,
        IList<BattleSquad> defenders,
        int battleSeed)
    {
        GameRulesData rules = GameRulesLoader.Load(RulesDatabaseFixture.DatabasePath);
        RNG.Reset(battleSeed);
        StaticRNG random = new();
        BattleAftermathDependencies aftermath = new(
            new Date(1, 1, 1),
            random,
            NoOpPlayerBattleAftermathSink.Instance);
        BattleExecutionContext execution = new(rules, random, aftermath);
        return new BattleTurnResolver(
            grid,
            attackers,
            defenders,
            region: null,
            execution,
            new BattleSideProfile(Aggression.Aggressive, BattleRole.Attacker),
            new BattleSideProfile(Aggression.Aggressive, BattleRole.Defender));
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
