
namespace OnlyWar.Models.Missions
{
    public interface IMissionStep
    {
        public string Description { get; }
        
        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep);
    }

    public interface  ITestMissionStep : IMissionStep
    {
        public IMissionTest MissionTest { get; }
        public IMissionStep StepIfSuccess { get; }
        public IMissionStep StepIfFailure { get; }
    }
}
