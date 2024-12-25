using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Models.Orders
{
    public class AttackRegionOrder: IOrder
    {
        public int Id { get; }
        public Region TargetRegion { get; }
        public OrderType OrderType { get { return OrderType.AttackRegion; } }
        public Squad OrderedSquad { get; }


        public AttackRegionOrder(int id, Squad orderedSquad, Region targetRegion)
        {
            Id = id;
            TargetRegion = targetRegion;
            OrderedSquad = orderedSquad;
        }
    }
}
