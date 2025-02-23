using OnlyWar.Models.Missions;

namespace OnlyWar.Helpers.Missions
{
    public interface IMissionStep
    {
        public string Description { get; }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep);
    }

    public abstract class ATestMissionStep : IMissionStep
    {
        public abstract string Description { get; }

        public abstract void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep);

        protected IMissionCheck _missionTest;
    }
}
