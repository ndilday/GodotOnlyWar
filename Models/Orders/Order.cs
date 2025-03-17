using OnlyWar.Builders;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Squads;
using System.Collections.Generic;

namespace OnlyWar.Models.Orders
{
    // A squad order has three main descriptors:
    // 1) whether the squad is dug in, mobile, or raiding (moving and then returning);
    // 2) Whether the squad is actively engaging, or focused on observing; and
    // 3) Whether the squad is attempting to stay hidden or not
    // Level of Aggression impacts how favorite the circumstances must be for the squad to choose to engage

    public class Order
    {
        public int Id { get; }
        public List<Squad> AssignedSquads { get; }
        public Disposition Disposition { get; }
        public bool IsQuiet { get; }
        public bool IsActivelyEngaging { get; }
        public Aggression LevelOfAggression { get; }
        public Mission Mission { get; }

        public Order(List<Squad> orderedSquads, Disposition disposition, bool isQuiet, bool isActivelyEngaging, Aggression levelOfAggression, Mission mission) 
            : this(IdGenerator.GetNextOrderId(), orderedSquads, disposition, isQuiet, isActivelyEngaging, levelOfAggression, mission) {}

        public Order(int id, List<Squad> orderedSquads, Disposition disposition, bool isQuiet, bool isActivelyEngaging, Aggression levelOfAggression, Mission mission)
        {
            Id = id;
            AssignedSquads = orderedSquads; 
            Disposition = disposition;
            IsQuiet = isQuiet;
            IsActivelyEngaging = isActivelyEngaging;
            LevelOfAggression = levelOfAggression;
            Mission = mission;
        }
    }

    public enum Disposition
    {
        DugIn = 0,
        Mobile = 1,
        Raiding = 2
    }

    public enum Aggression
    {
        Avoid = 0,
        Cautious = 1,
        Normal = 2,
        Attritional = 3,
        Aggressive = 4
    }
}
