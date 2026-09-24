using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Battles;
using OnlyWar.Battles.Aftermath;
using OnlyWar.Battles.Models;
using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Tests.Fixtures;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Drives <see cref="BattleMoraleService"/> turn by turn without the resolver, the way the
/// resolver does: snapshot at turn start, casualties during the turn, the check at the end. The
/// RNG hands out scripted z-values, so each soldier's pass or fail is chosen by the test.
/// </summary>
internal sealed class MoraleServiceHarness
{
    /// <summary>A z this large fails every soldier; its negation passes every soldier.</summary>
    public const double Fail = 10.0;
    public const double Hold = -10.0;

    /// <summary>Returns queued z-values in order, then <see cref="DefaultZ"/>.</summary>
    public sealed class ScriptedRNG : IRNG
    {
        private readonly Queue<double> _queue = new();
        public double DefaultZ { get; set; }
        public void Enqueue(params double[] values)
        {
            foreach (double value in values) _queue.Enqueue(value);
        }
        public double GetDoubleInRange(double lowerBound, double upperBound) => lowerBound;
        public double GetLinearDouble() => 0.0;
        public int GetIntBelowMax(int min, int max) => min;
        public double NextRandomZValue() => _queue.Count > 0 ? _queue.Dequeue() : DefaultZ;
    }

    private sealed class NoOpPlayerBattleAftermathSink : IPlayerBattleAftermathSink
    {
        public void MoveToFallenBrothers(PlayerSoldier soldier) { }
        public void AddRecoveredGeneseed(float purity) { }
        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) { }
    }

    private readonly BattleGridManager _grid;

    public BattleState State { get; }
    public BattleMoraleService Service { get; }
    public ScriptedRNG Random { get; } = new();

    public MoraleServiceHarness(
        BattleGridManager grid,
        IEnumerable<BattleSquad> attackers,
        IEnumerable<BattleSquad> opposing)
    {
        _grid = grid;
        State = new BattleState(
            attackers.ToDictionary(squad => squad.Id),
            opposing.ToDictionary(squad => squad.Id));
        GameRulesData rules = OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(
            RulesDatabaseFixture.DatabasePath);
        BattleExecutionContext execution = new(
            rules,
            Random,
            new BattleAftermathDependencies(
                new Date(1, 1, 1), Random, new NoOpPlayerBattleAftermathSink()));
        Service = new BattleMoraleService(
            State,
            grid,
            execution,
            State.AllAttackerSquads.Values.Concat(State.AllOpposingSquads.Values).ToList());
    }

    /// <summary>The live copy of a squad. BattleState deep-copies the squads it is given.</summary>
    public BattleSquad Live(BattleSquad original) =>
        State.AllAttackerSquads.TryGetValue(original.Id, out BattleSquad attacker)
            ? attacker
            : State.AllOpposingSquads[original.Id];

    /// <summary>
    /// Advances the turn and takes the turn-start snapshot. Planning consumes any pending mob
    /// coercion before the next check, so it is cleared here too.
    /// </summary>
    public void StartTurn()
    {
        State.AdvanceTurn();
        foreach (BattleSquad squad in State.AllAttackerSquads.Values.Concat(State.AllOpposingSquads.Values))
        {
            squad.MobSuppressionPending = false;
        }
        Service.SnapshotTurnStart();
    }

    /// <summary>Removes able soldiers from the live squad: troops first, the leader last.</summary>
    public void Kill(BattleSquad original, int count)
    {
        BattleSquad squad = Live(original);
        foreach (BattleSoldier soldier in squad.AbleSoldiers
            .OrderBy(soldier => soldier.Soldier.Template.IsSquadLeader)
            .Take(count)
            .ToList())
        {
            squad.RemoveSoldier(soldier);
            _grid.RemoveSoldier(soldier.Soldier.Id);
        }
    }

    /// <summary>Runs the end-of-turn check for one side against even force metrics.</summary>
    public List<BattleEvent> Check(BattleSide side)
    {
        BattleForceMetrics metrics = new(100, 100, 0, 5, 0f, 0f, 0, false, true, true);
        List<BattleEvent> events = [];
        Service.EvaluateSide(side, metrics, metrics, events);
        return events;
    }

    public static bool Has(IEnumerable<BattleEvent> events, BattleEventType type, BattleSquad squad = null) =>
        events.Any(e => e.Type == type && (squad == null || e.PrimarySquadId == squad.Id));

    public static Faction CreateFaction(int id, FactionBehavior behavior) => new(
        id,
        $"Faction {id}",
        Color.Green,
        isPlayerFaction: false,
        isDefaultFaction: false,
        behavior,
        GrowthType.None,
        new Dictionary<int, Species>(),
        new Dictionary<int, SoldierTemplate>(),
        new Dictionary<int, SquadTemplate>(),
        new Dictionary<int, UnitTemplate>(),
        new Dictionary<int, BoatTemplate>(),
        new Dictionary<int, ShipTemplate>(),
        new Dictionary<int, FleetTemplate>());

    /// <summary>
    /// A squad of Ego-8 troops (resolve 0.95), led by a sergeant when <paramref name="withLeader"/>
    /// is set. The leader is the first soldier, so he takes the first z-value of each roll.
    /// </summary>
    public static BattleSquad CreateSquad(
        string name,
        int templateId,
        Faction faction,
        int troops,
        bool withLeader,
        SquadTypes squadType = SquadTypes.None,
        SoldierTemplate troopTemplate = null)
    {
        troopTemplate ??= TestModelFactory.MarineTemplate;
        List<SquadTemplateElement> elements = [new(troopTemplate, 0, (byte)troops)];
        if (withLeader)
        {
            elements.Insert(0, new SquadTemplateElement(TestModelFactory.SergeantTemplate, 0, 1));
        }
        SquadTemplate template = new(
            templateId,
            $"{name} Template",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            elements,
            squadType)
        {
            Faction = faction
        };
        Squad squad = new(templateId, name, null, template);
        int count = troops + (withLeader ? 1 : 0);
        for (int i = 0; i < count; i++)
        {
            SoldierTemplate soldierTemplate = withLeader && i == 0
                ? TestModelFactory.SergeantTemplate
                : troopTemplate;
            Soldier soldier = TestModelFactory.CreateSoldier(template: soldierTemplate, name: $"{name} {i + 1}");
            soldier.Id = (templateId * 100) + i;
            soldier.Ego = 8f;
            squad.AddSquadMember(soldier);
        }
        return new BattleSquad(false, squad);
    }

    public static void Place(BattleGridManager grid, BattleSquad squad, bool side, int x, int y)
    {
        for (int i = 0; i < squad.Soldiers.Count; i++)
        {
            BattleSoldier soldier = squad.Soldiers[i];
            soldier.TopLeft = new ValueTuple<int, int>(x + i, y);
            grid.PlaceSoldier(soldier, side, [soldier.TopLeft.Value]);
        }
    }
}
