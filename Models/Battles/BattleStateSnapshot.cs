using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Battles
{
    /// <summary>
    /// Compact immutable state retained for battle replay. The live simulation keeps one mutable
    /// <see cref="BattleState"/>; replay turns retain only presentation fields rather than cloning
    /// tactical equipment collections and other simulation-only state for every surviving soldier.
    /// </summary>
    public sealed class BattleStateSnapshot
    {
        public int TurnNumber { get; }
        public IReadOnlyDictionary<int, BattleSoldierSnapshot> Soldiers { get; }
        public IReadOnlyDictionary<int, BattleSquadSnapshot> AttackerSquads { get; }
        public IReadOnlyDictionary<int, BattleSquadSnapshot> OpposingSquads { get; }

        private BattleStateSnapshot(
            int turnNumber,
            IReadOnlyDictionary<int, BattleSoldierSnapshot> soldiers,
            IReadOnlyDictionary<int, BattleSquadSnapshot> attackerSquads,
            IReadOnlyDictionary<int, BattleSquadSnapshot> opposingSquads)
        {
            TurnNumber = turnNumber;
            Soldiers = soldiers;
            AttackerSquads = attackerSquads;
            OpposingSquads = opposingSquads;
        }

        public static BattleStateSnapshot Capture(BattleState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            Dictionary<int, BattleSoldierSnapshot> soldierSnapshots = [];
            BattleSoldierSnapshot CaptureSoldier(BattleSoldier soldier)
            {
                if (!soldierSnapshots.TryGetValue(soldier.Soldier.Id, out BattleSoldierSnapshot snapshot))
                {
                    snapshot = new BattleSoldierSnapshot(soldier);
                    soldierSnapshots[soldier.Soldier.Id] = snapshot;
                }
                return snapshot;
            }

            Dictionary<int, BattleSquadSnapshot> attackerSquads = state.AttackerSquads
                .ToDictionary(
                    pair => pair.Key,
                    pair => new BattleSquadSnapshot(pair.Value, pair.Value.Soldiers.Select(CaptureSoldier).ToList()));
            Dictionary<int, BattleSquadSnapshot> opposingSquads = state.OpposingSquads
                .ToDictionary(
                    pair => pair.Key,
                    pair => new BattleSquadSnapshot(pair.Value, pair.Value.Soldiers.Select(CaptureSoldier).ToList()));

            // The live BattleState intentionally retains soldiers incapacitated during the current
            // turn until after its replay snapshot is captured. They may have recorded actions and
            // the chronicle still needs their identity and last position for that round.
            foreach (BattleSoldier soldier in state.Soldiers.Values)
            {
                CaptureSoldier(soldier);
            }

            return new BattleStateSnapshot(
                state.TurnNumber,
                soldierSnapshots,
                attackerSquads,
                opposingSquads);
        }

        internal static BattleStateSnapshot FromSquads(
            int turnNumber,
            IEnumerable<BattleSquadSnapshot> attackerSquads,
            IEnumerable<BattleSquadSnapshot> opposingSquads)
        {
            Dictionary<int, BattleSquadSnapshot> attackerMap = attackerSquads.ToDictionary(squad => squad.Id);
            Dictionary<int, BattleSquadSnapshot> opposingMap = opposingSquads.ToDictionary(squad => squad.Id);
            Dictionary<int, BattleSoldierSnapshot> soldiers = attackerMap.Values
                .Concat(opposingMap.Values)
                .SelectMany(squad => squad.Soldiers)
                .GroupBy(soldier => soldier.Id)
                .ToDictionary(group => group.Key, group => group.First());
            return new BattleStateSnapshot(turnNumber, soldiers, attackerMap, opposingMap);
        }
    }

    public sealed class BattleSquadSnapshot
    {
        public int Id { get; }
        public string Name { get; }
        public bool IsPlayerSquad { get; }
        public bool IsPlayerAligned { get; }
        public bool IsInMelee { get; }
        public Squad Squad { get; }
        public IReadOnlyList<BattleSoldierSnapshot> Soldiers { get; }

        internal BattleSquadSnapshot(BattleSquad squad, IReadOnlyList<BattleSoldierSnapshot> soldiers)
        {
            Id = squad.Id;
            Name = squad.Name;
            IsPlayerSquad = squad.IsPlayerSquad;
            IsPlayerAligned = squad.IsPlayerAligned;
            IsInMelee = squad.IsInMelee;
            Squad = squad.Squad;
            Soldiers = soldiers;
        }
    }

    public sealed class BattleSoldierSnapshot
    {
        public int Id { get; }
        public ISoldier Soldier { get; }
        public int SquadId { get; }
        public string SquadName { get; }
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Depth { get; }
        public bool IsInMelee { get; }
        public float TurnsRunning { get; }
        public ushort TurnsShooting { get; }

        public int MinX => X;
        public int MaxX => X + Width - 1;
        public int MinY => Y - Depth;
        public int MaxY => Y - 1;
        public float CenterX => (MinX + MaxX) / 2.0f;
        public float CenterY => (MinY + MaxY) / 2.0f;

        internal BattleSoldierSnapshot(BattleSoldier soldier)
        {
            Soldier = soldier.Soldier;
            Id = soldier.Soldier.Id;
            SquadId = soldier.BattleSquad?.Id ?? 0;
            SquadName = soldier.BattleSquad?.Name ?? "Unknown formation";
            X = soldier.TopLeft?.Item1 ?? 0;
            Y = soldier.TopLeft?.Item2 ?? 0;
            bool isRotated = soldier.Orientation % 2 != 0;
            Width = isRotated
                ? soldier.Soldier.Template.Species.Depth
                : soldier.Soldier.Template.Species.Width;
            Depth = isRotated
                ? soldier.Soldier.Template.Species.Width
                : soldier.Soldier.Template.Species.Depth;
            IsInMelee = soldier.IsInMelee;
            TurnsRunning = soldier.TurnsRunning;
            TurnsShooting = soldier.TurnsShooting;
        }

        public IEnumerable<Tuple<int, int>> GetPositions()
        {
            for (int x = MinX; x <= MaxX; x++)
            {
                for (int y = MinY; y <= MaxY; y++)
                {
                    yield return new Tuple<int, int>(x, y);
                }
            }
        }
    }
}
