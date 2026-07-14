using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using System.Collections.Generic;

namespace OnlyWar.Models.Battles
{
    public class BattleTurn
    {
        public int TurnNumber { get { return State.TurnNumber; } }
        public BattleStateSnapshot State { get; }
        public IReadOnlyList<IAction> Actions { get; }

        public BattleTurn(BattleState state, IReadOnlyList<IAction> actions)
        {
            State = BattleStateSnapshot.Capture(state);
            Actions = actions;
        }
     }
}
