using OnlyWar.Models.Battles;

namespace OnlyWar.Helpers.Battles.Actions
{
    public interface IAction
    {
        void Execute(BattleState state);
        string Description();
    }
}
