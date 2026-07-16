using OnlyWar.Models.Battles;
using System;

namespace OnlyWar.Helpers.Battles.Actions
{
    public class MoveAction : IAction
    {
        private readonly BattleSoldier _soldier;
        private readonly BattleGridManager _grid;
        private readonly Tuple<int, int> _newTopLeft, _currentTopLeft;
        private readonly ushort _newOrientation;
        private readonly float _movementBudget;

        public int ActorId => _soldier.Soldier.Id;

        public MoveAction(BattleSoldier soldier, BattleGridManager grid, Tuple<int, int> currentTopLeft, Tuple<int, int> newTopLeft, ushort orientation)
            : this(
                soldier,
                grid,
                currentTopLeft,
                newTopLeft,
                orientation,
                CalculateDistance(currentTopLeft, newTopLeft))
        {
        }

        public MoveAction(
            BattleSoldier soldier,
            BattleGridManager grid,
            Tuple<int, int> currentTopLeft,
            Tuple<int, int> newTopLeft,
            ushort orientation,
            float movementBudget)
        {
            _soldier = soldier;
            _grid = grid;
            _newTopLeft = newTopLeft;
            _newOrientation = orientation;
            _currentTopLeft = currentTopLeft;
            _movementBudget = Math.Max(0, movementBudget);
        }

        public void Execute(BattleState state)
        {
            //_resultList.Add(new MoveResolution(_soldier, _grid, _newTopLeft, _orientation));
            if (_grid.TryMoveSoldier(_soldier, _newTopLeft, _newOrientation))
            {
                _soldier.TopLeft = _newTopLeft;
                _soldier.Orientation = _newOrientation;
                _soldier.TurnsRunning++;
                _soldier.LeftoverMovement = Math.Max(
                    0,
                    _movementBudget - CalculateDistance(_currentTopLeft, _newTopLeft));
            }
            else
            {
                // Congestion is another artifact of resolving continuous movement on an
                // integer grid. Preserve the full declared budget for a later moving turn.
                _soldier.LeftoverMovement = _movementBudget;
            }
        }

        private static float CalculateDistance(Tuple<int, int> from, Tuple<int, int> to)
        {
            int x = to.Item1 - from.Item1;
            int y = to.Item2 - from.Item2;
            return (float)Math.Sqrt((x * x) + (y * y));
        }

        public string Description()
        {
            return $"{_soldier.Soldier.Name} moves from ({_currentTopLeft.Item1}, {_currentTopLeft.Item2}) to ({_newTopLeft.Item1}, {_newTopLeft.Item2})\n";
        }
    }
}
