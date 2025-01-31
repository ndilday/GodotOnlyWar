using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using System.Collections.Generic;

namespace OnlyWar.Models.Battles
{
    public class BattleTurn
    {
        public int TurnNumber { get { return State.TurnNumber; } }
        public BattleState State { get; private set; }
        public IReadOnlyList<IAction> Actions { get; }

        public BattleTurn(BattleState state, IReadOnlyList<IAction> actions)
        {
            State = state;
            Actions = actions;
        }
     }
}
