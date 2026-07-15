using OnlyWar.Models;
using System;

namespace OnlyWar.Helpers.Battles.Aftermath
{
    internal sealed class BattleAftermathDependencies
    {
        public Date Date { get; }
        public IRNG Random { get; }
        public IPlayerBattleAftermathSink PlayerSink { get; }

        public BattleAftermathDependencies(
            Date date,
            IRNG random,
            IPlayerBattleAftermathSink playerSink)
        {
            Date = date ?? throw new ArgumentNullException(nameof(date));
            Random = random ?? throw new ArgumentNullException(nameof(random));
            PlayerSink = playerSink ?? throw new ArgumentNullException(nameof(playerSink));
        }
    }
}
