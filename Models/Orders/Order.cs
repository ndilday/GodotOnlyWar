using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;

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
        public Squad OrderedSquad { get; }
        public Region TargetRegion { get; }
        public Disposition Disposition { get; }
        public bool IsQuiet { get; }
        public bool IsActivelyEngaging { get; }
        public Aggression LevelOfAggression { get; }
        public MissionType MissionType { get; }

        public Order(int id, Squad orderedSquad, Region targetRegion, Disposition disposition, bool isQuiet, bool isActivelyEngaging, Aggression levelOfAggression, MissionType missionType)
        {
            Id = id;
            OrderedSquad = orderedSquad;
            Disposition = disposition;
            IsQuiet = isQuiet;
            IsActivelyEngaging = isActivelyEngaging;
            LevelOfAggression = levelOfAggression;
            TargetRegion = targetRegion;
            MissionType = missionType;
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

    public enum MissionType
    {
        Default = 0,
        Train,
        Sabotage,
        Assassination,
        Ambush,
        Recovery
    }
}
