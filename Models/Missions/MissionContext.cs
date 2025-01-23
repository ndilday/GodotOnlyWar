using OnlyWar.Models.Squads;

namespace OnlyWar.Models.Missions
{
    public class MissionContext
    {
        public Squad Squad { get; }
        public ushort DaysElapsed { get; set; }
    }
}
