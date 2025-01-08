using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Models.Orders
{
    internal class LandInRegionOrder : IOrder
    {
        public int Id { get; }

        public OrderType OrderType{ get { return OrderType.LandInRegion; } }

        public Region TargetRegion { get; }

        public Squad OrderedSquad { get; }

        public LandInRegionOrder(int id, Region targetRegion, Squad orderedSquad)
        {
            Id = id;
            TargetRegion = targetRegion;
            OrderedSquad = orderedSquad;
        }
    }
}
