using OnlyWar.Models;
using OnlyWar.Models.Events;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using System;

namespace OnlyWar.Helpers.Battles.Aftermath
{
    internal sealed class BattleAftermathDependencies
    {
        public Date Date { get; }
        public IRNG Random { get; }
        public IPlayerBattleAftermathSink PlayerSink { get; }
        private int _battleOrdinal;

        public BattleAftermathDependencies(
            Date date,
            IRNG random,
            IPlayerBattleAftermathSink playerSink)
        {
            Date = date ?? throw new ArgumentNullException(nameof(date));
            Random = random ?? throw new ArgumentNullException(nameof(random));
            PlayerSink = playerSink ?? throw new ArgumentNullException(nameof(playerSink));
        }

        internal BattleEventContextSnapshot CreateBattleEventContext(
            Region region,
            Order order)
        {
            Mission mission = order?.Mission;
            Planet planet = region?.Planet;
            string correlationKey = $"battle/{Date.GetTotalWeeks()}/{region?.Id ?? 0}/"
                + $"{order?.Id ?? 0}/{_battleOrdinal++}";
            return new BattleEventContextSnapshot(
                correlationKey,
                Date.GetTotalWeeks(),
                region?.Id,
                region?.Name,
                planet?.Id,
                planet?.Name,
                MissionId: mission?.Id,
                MissionName: mission?.MissionType.ToString(),
                MissionType: mission?.MissionType,
                OrderId: order?.Id,
                OrderName: order == null ? null : $"Order {order.Id}",
                Aggression: order?.LevelOfAggression);
        }
    }
}
