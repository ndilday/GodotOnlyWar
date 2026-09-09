using OnlyWar.Battles.Resolutions;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Soldiers;

namespace OnlyWar.Battles.Aftermath
{
    internal sealed class NpcBattleAftermathPolicy : IBattleAftermathPolicy
    {
        public static NpcBattleAftermathPolicy Instance { get; } = new();

        private NpcBattleAftermathPolicy()
        {
        }

        public void OnSoldierDowned(WoundResolution wound, WoundLevel woundLevel)
        {
        }

        public void OnSoldierKilled(WoundResolution wound, WoundLevel woundLevel)
        {
        }

        public void OnBattleCompleted(BattleState finalState)
        {
        }
    }
}
