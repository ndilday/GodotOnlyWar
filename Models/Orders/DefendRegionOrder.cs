using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;

namespace OnlyWar.Models.Orders
{
    public class DefendRegionOrder: IOrder
    {
        public int Id { get; }
        public Region TargetRegion { get; }
        public OrderType OrderType { get { return OrderType.DefendBorder; } }
        public Squad OrderedSquad { get; }

        public Region BorderToDefend { get; }

        public DefendRegionOrder(int id, Squad orderedSquad, Region targetRegion, Region borderToDefend)
        {
            Id = id;
            TargetRegion = targetRegion;
            OrderedSquad = orderedSquad;
            BorderToDefend = borderToDefend;
        }
    }
}
