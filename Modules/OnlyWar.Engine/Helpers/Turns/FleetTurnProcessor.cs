using OnlyWar.Models;
using OnlyWar.Models.Fleets;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Advances task-force travel for one campaign week and resolves the training earned
    /// during subjective warp time when a task force exits the warp.
    /// </summary>
    internal sealed class FleetTurnProcessor
    {
        private readonly ChapterUpkeepProcessor _chapterUpkeepProcessor;

        internal FleetTurnProcessor(ChapterUpkeepProcessor chapterUpkeepProcessor)
        {
            _chapterUpkeepProcessor = chapterUpkeepProcessor;
        }

        internal void AdvanceFleetMovement(Sector sector)
        {
            foreach (TaskForce taskForce in sector.Fleets.Values)
            {
                FleetTravelAdvanceResult result = taskForce.AdvanceTravelOneWeek();
                if (result.ExitedWarp)
                {
                    _chapterUpkeepProcessor.ApplyWarpSubjectiveTraining(
                        taskForce,
                        result.WarpSubjectiveWeeksElapsed);
                    taskForce.WarpSubjectiveTrainingApplied = true;
                }
            }
        }
    }
}
