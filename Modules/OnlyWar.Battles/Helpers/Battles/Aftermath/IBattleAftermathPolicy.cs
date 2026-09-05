using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles.Aftermath
{
    internal interface IBattleAftermathPolicy
    {
        void OnSoldierDowned(WoundResolution wound, WoundLevel woundLevel);
        void OnSoldierKilled(WoundResolution wound, WoundLevel woundLevel);
        void OnBattleCompleted(BattleState finalState);
    }
}
