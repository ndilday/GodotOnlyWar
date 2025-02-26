using OnlyWar.Models.Missions;

namespace OnlyWar.Helpers.Missions
{
    public interface IMissionStep
    {
        public string Description { get; }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep);
    }
}
