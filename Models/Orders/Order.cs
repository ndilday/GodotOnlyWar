using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;

namespace OnlyWar.Models.Orders
{
    public interface IOrder
    {
        int Id { get; }
        // order type
        OrderType OrderType { get; }
        Region TargetRegion { get; }
        Squad OrderedSquad { get; }
        // Move to Region
        // Defend Region Border
        // Land in Region
        // Convert Population
        // Exterminate Population
        // Hunt for Hidden enemies/Patrol
        // In Reserve
        // Stand Down
    }

    public enum OrderType
    {
        MoveToRegion,
        DefendBorder,
        LandInRegion,
        ConvertPopulation,
        ExterminatePopulation,
        Hunt,
        Reserve,
        Hide,
        Train
    }
}
