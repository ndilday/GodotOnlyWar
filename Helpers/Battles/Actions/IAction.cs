using OnlyWar.Helpers.Battles;

namespace OnlyWar.Helpers.Battles.Actions
{
    public interface IAction
    {
        void Execute(BattleState state);
    }
}
