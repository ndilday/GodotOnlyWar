using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles.Aftermath
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
