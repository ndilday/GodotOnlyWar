using System.Linq;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles.Aftermath
{
    internal static class BattleAftermathPolicyFactory
    {
        public static IBattleAftermathPolicy Create(BattleAftermathContext context)
        {
            return context.StartingSoldiers.Any(soldier => soldier.Soldier is PlayerSoldier)
                ? new PlayerChapterBattleAftermathPolicy(context)
                : NpcBattleAftermathPolicy.Instance;
        }
    }
}
