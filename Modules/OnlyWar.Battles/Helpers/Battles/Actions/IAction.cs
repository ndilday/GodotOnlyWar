using OnlyWar.Battles.Models;

namespace OnlyWar.Battles.Actions
{
    public interface IAction
    {
        void Execute(BattleState state);
        string Description();
        int ActorId { get; }
    }
}
