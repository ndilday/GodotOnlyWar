using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;

namespace OnlyWar.Models.Orders
{
    public class SabotageOrder : Order
    {
        public DefenseType DefenseType { get; private set; }
        public int TargetSize { get; private set; }

        public SabotageOrder(Squad orderedSquad, Region targetRegion, Disposition disposition, bool isQuiet, bool isActivelyEngaging, Aggression levelOfAggression, DefenseType defenseType, int targetSize) 
            : base(orderedSquad, targetRegion, disposition, isQuiet, isActivelyEngaging, levelOfAggression, MissionType.Sabotage)
        {
            DefenseType = defenseType;
            TargetSize = targetSize;
        }
    }
}
