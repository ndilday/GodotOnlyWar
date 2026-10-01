namespace OnlyWar.Operations.Missions
{
    /// <summary>
    /// The end of a Move: the force is across the border and the mission is over.
    /// </summary>
    /// <remarks>
    /// The step itself only records the arrival. The squads are moved when the mission ends, by
    /// MissionForceRelocation.ResolveHeldGround - the same place an Advance's squads are moved - so
    /// both orders share one rule for when a force takes up residence in the region it reached.
    ///
    /// It spends no day. The crossing (InfiltrateMissionStep) was the day's work, and the days left in
    /// the week become training credit for a squad that is no longer deployed.
    /// </remarks>
    public class ArriveMissionStep : IMissionStep
    {
        public string Description { get { return "Arrive"; } }

        public MissionStepResult ExecuteMissionStep(
            MissionExecutionContext execution,
            float marginOfSuccess,
            IMissionStep resumeStep)
        {
            MissionContext context = execution.State;
            context.AddLog(
                $"Day {context.DaysElapsed}: Force has taken up position in "
                + $"{context.Order.Mission.RegionFaction.Region.Name}.");
            return MissionStepResult.Complete;
        }
    }
}
