using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Battles.Aftermath
{
    internal interface IPlayerBattleAftermathSink
    {
        void MoveToFallenBrothers(PlayerSoldier soldier);

        void AddRecoveredGeneseed(float purity);

        void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents);
    }
}
