using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;

namespace OnlyWar.Models.Orders
{
    public class AssassinationOrder : Order
    {
        public int TargetSize { get; private set; }

        public AssassinationOrder(Squad orderedSquad, Region targetRegion, Disposition disposition, bool isQuiet, bool isActivelyEngaging, Aggression levelOfAggression, int targetSize)
            : base(orderedSquad, targetRegion, disposition, isQuiet, isActivelyEngaging, levelOfAggression, MissionType.Sabotage)
        {
            TargetSize = targetSize;
        }
    }
}
