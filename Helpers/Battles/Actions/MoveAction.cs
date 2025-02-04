using OnlyWar.Models.Battles;
using OnlyWar.Helpers.Battles.Resolutions;
using System;
using System.Collections.Concurrent;

namespace OnlyWar.Helpers.Battles.Actions
{
    public class MoveAction : IAction
    {
        private readonly BattleSoldier _soldier;
        private readonly BattleGridManager _grid;
        private readonly Tuple<int, int> _newTopLeft, _currentTopLeft;
        private readonly ushort _newOrientation;

        public int ActorId => _soldier.Soldier.Id;

        public MoveAction(BattleSoldier soldier, BattleGridManager grid, Tuple<int, int> currentTopLeft, Tuple<int, int> newTopLeft, ushort orientation)
        {
            _soldier = soldier;
            _grid = grid;
            _newTopLeft = newTopLeft;
            _newOrientation = orientation;
            _currentTopLeft = currentTopLeft;
        }

        public void Execute(BattleState state)
        {
            //_resultList.Add(new MoveResolution(_soldier, _grid, _newTopLeft, _orientation));
            _soldier.TopLeft = _newTopLeft;
            _soldier.Orientation = _newOrientation;
            _grid.MoveSoldier(_soldier, _newTopLeft, _newOrientation);
            _soldier.TurnsRunning++;
        }

        public string Description()
        {
            return $"{_soldier.Soldier.Name} moves from ({_currentTopLeft.Item1}, {_currentTopLeft.Item2}) to ({_newTopLeft.Item1}, {_newTopLeft.Item2})";
        }
    }
}
