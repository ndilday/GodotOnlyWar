using OnlyWar.Domain;
using OnlyWar.Domain.Soldiers;
using System.Collections.Generic;

namespace OnlyWar.Battles.Abstractions
{
    public interface IPlayerBattleAftermathSink
    {
        void MoveToFallenBrothers(PlayerSoldier soldier);

        void AddRecoveredGeneseed(float purity);

        void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents);
    }
}
