using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Battles
{
    public class BattleTurn
    {
        public int TurnNumber { get { return State.TurnNumber; } }
        public BattleStateSnapshot State { get; }
        public IReadOnlyList<IAction> Actions { get; }
        public IReadOnlyList<BattleEvent> Events { get; }

        public BattleTurn(BattleState state, IReadOnlyList<IAction> actions)
            : this(state, actions, null)
        {
        }

        public BattleTurn(BattleState state, IReadOnlyList<IAction> actions, IReadOnlyList<BattleEvent> events)
            : this(state, actions, events, null)
        {
        }

        internal BattleTurn(
            BattleState state,
            IReadOnlyList<IAction> actions,
            IReadOnlyList<BattleEvent> events,
            IEnumerable<BattleSoldier> retainedSoldiers)
        {
            State = BattleStateSnapshot.Capture(state, retainedSoldiers);
            Actions = (actions ?? new List<IAction>()).ToArray();
            Events = (events ?? new List<BattleEvent>()).ToArray();
        }
     }
}
