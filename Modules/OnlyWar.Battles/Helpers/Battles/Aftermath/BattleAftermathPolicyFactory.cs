using System;
using System.Linq;
using OnlyWar.Domain.Soldiers;

namespace OnlyWar.Battles.Aftermath
{
    internal static class BattleAftermathPolicyFactory
    {
        public static IBattleAftermathPolicy Create(BattleAftermathContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return context.StartingSoldiers.Any(soldier => soldier.Soldier is PlayerSoldier)
                ? new PlayerChapterBattleAftermathPolicy(context, context.Dependencies)
                : NpcBattleAftermathPolicy.Instance;
        }
    }
}
