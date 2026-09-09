using OnlyWar.Battles.Resolutions;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Soldiers;

namespace OnlyWar.Battles.Aftermath
{
    internal interface IBattleAftermathPolicy
    {
        void OnSoldierDowned(WoundResolution wound, WoundLevel woundLevel);
        void OnSoldierKilled(WoundResolution wound, WoundLevel woundLevel);
        void OnBattleCompleted(BattleState finalState);
    }
}
