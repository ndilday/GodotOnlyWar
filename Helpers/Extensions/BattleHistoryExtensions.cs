using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Battles;
using System.Text;

namespace OnlyWar.Helpers.Extensions
{
    public static class BattleHistoryExtensions
    {
        public static string GetBattleLog(this BattleHistory battleHistory)
        {
            StringBuilder sb = new StringBuilder();
            foreach (BattleTurn turn in battleHistory.Turns)
            {
                sb.Append(turn.GetBattleLog());
            }
            return sb.ToString();
        }

        public static string GetBattleLog(this BattleTurn battleTurn)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"Turn {battleTurn.TurnNumber}\n");
            foreach (IAction action in battleTurn.Actions)
            {
                sb.Append(action.Description());
            }

            return sb.ToString();
        }
    }
}
