using OnlyWar.Models.Missions;

namespace OnlyWar.Helpers.Missions
{
    /// <summary>
    /// When during a day a step resolves. The day scheduler runs every active mission's Shaping
    /// steps before any Acting step, so a mission that SHAPES the ground a region's occupants are
    /// watching resolves before the missions that have to cross it.
    /// </summary>
    /// <remarks>
    /// This is declared on the STEP, never keyed off MissionType, and that is the whole point. A rule
    /// of the form "if (MissionType.Diversion) go first" would rebuild the old phase-ordered shaping
    /// pass at day granularity - a hardcoded interaction between two named mission types. Declaring
    /// it here means a loud assault approach, or a sabotage that wrecks a listening post before that
    /// day's infiltrators roll, interact for free and nothing in the scheduler knows what a diversion
    /// is. See OnlyWar_TDD.md §6.4
    /// </remarks>
    public enum MissionStepPhase
    {
        Shaping,
        Acting
    }

    /// <summary>
    /// What a step decided to do next, instead of calling its successor directly.
    /// </summary>
    /// <remarks>
    /// Steps used to be continuation-passing: each one invoked the next inside its own call, so a
    /// whole mission ran to completion in a single stack and no two missions could be part-way
    /// through the same day. Returning the successor turns the chain into a trampoline
    /// (<see cref="MissionStepDriver"/>), which is what lets the day scheduler interleave missions.
    ///
    /// Four fields, because the old signature carried four things:
    /// <list type="bullet">
    /// <item><description><c>Next</c> - the successor, or null when this chain is finished.</description></item>
    /// <item><description><c>Margin</c> - the margin handed to the successor, previously the
    /// <c>marginOfSuccess</c> argument.</description></item>
    /// <item><description><c>Resume</c> - the old <c>returnStep</c>: where a detour (detection,
    /// interception, evasion) goes back to if the force survives it. Conditional by design; a step
    /// that ends the mission simply never resumes it.</description></item>
    /// <item><description><c>Then</c> - a MANDATORY follow-up, run when the successor's chain
    /// finishes however it finishes. This exists for the two steps whose successor call was not in
    /// tail position (LightningRaidMissionStep and PerformAssassinationMissionStep both ran an
    /// engagement and then withdrew regardless of its outcome). <c>Resume</c> cannot express that,
    /// because the engagement step deliberately declines to resume when the force is spent.</description></item>
    /// </list>
    /// </remarks>
    public readonly record struct MissionStepResult(
        IMissionStep Next,
        float Margin,
        IMissionStep Resume,
        IMissionStep Then)
    {
        /// <summary>This chain is finished. The driver runs any pending follow-up, then stops.</summary>
        public static MissionStepResult Complete => default;

        public static MissionStepResult Continue(
            IMissionStep next,
            float margin = 0f,
            IMissionStep resume = null,
            IMissionStep then = null) =>
            new(next, margin, resume, then);
    }

    public interface IMissionStep
    {
        public string Description { get; }

        /// <summary>
        /// Acting unless a step explicitly shapes the region. Defaulted so only the steps that
        /// genuinely shape have to say so.
        /// </summary>
        public MissionStepPhase Phase => MissionStepPhase.Acting;

        /// <summary>
        /// True for the steps that spend a day of mission time - the ones that increment
        /// <see cref="MissionContext.DaysElapsed"/>.
        /// </summary>
        /// <remarks>
        /// This makes an existing, undeclared convention explicit. Exactly 9 of the 18 steps increment
        /// the day counter, and they are precisely the "attempt something that takes a day" steps
        /// (infiltrate, exfiltrate, the three stealth approaches, ambush positioning and springing,
        /// the raid sweep, the feint). The other 9 are same-day resolution: a force spotted on day 3
        /// is intercepted, fights, and either breaks contact or is lost all within day 3.
        ///
        /// The scheduler needs the distinction because the increment marks the START of a day's
        /// activity, not its end. Everything a step chain does after an increment belongs to that day,
        /// so the day boundary is "the next step that would consume a day" - which cannot be
        /// discovered by running it. See MissionDayScheduler.
        /// </remarks>
        public bool ConsumesDay => false;

        public MissionStepResult ExecuteMissionStep(
            MissionExecutionContext execution,
            float marginOfSuccess,
            IMissionStep resumeStep);
    }
}
