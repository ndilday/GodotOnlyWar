using OnlyWar.Models.Missions;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Missions
{
    /// <summary>
    /// Drives one mission's step chain. Holds the position the chain has reached - which step is
    /// next, the margin owed to it, the pending resume target, and any mandatory follow-ups - so a
    /// mission can be advanced a step at a time instead of running to completion in one call stack.
    /// </summary>
    /// <remarks>
    /// This is the piece that makes daily interleaving possible. Previously each step invoked its own
    /// successor, so a mission's entire week resolved inside a single
    /// <c>ExecuteMissionStep</c> call and there was no point at which another mission in the same
    /// region could be part-way through the same day. Every cross-mission interaction therefore had
    /// to be expressed as phase ordering around whole missions. With the chain trampolined, the
    /// scheduler owns the days and missions yield.
    ///
    /// Phase 2 only uses <see cref="RunToCompletion"/>, which reproduces the old behaviour exactly
    /// (one mission at a time, start to finish). <see cref="AdvanceOneStep"/> is what Phase 3's day
    /// loop drives.
    /// </remarks>
    public sealed class MissionStepDriver
    {
        // The old chain recursed, so a runaway loop announced itself as a StackOverflowException. A
        // trampoline would instead spin silently forever, so the bound is explicit. Day budgets
        // (MissionContext.OperatingDaysSpent, the exfiltration grace) are the real governor; this is
        // only the backstop for a step chain that fails to advance them, and it is set far above any
        // legitimate mission (a contested week runs on the order of 30-40 steps).
        private const int MaxStepsPerMission = 500;

        private readonly MissionExecutionContext _execution;
        private readonly Stack<IMissionStep> _followUps = new();

        private IMissionStep _next;
        private IMissionStep _resume;
        private float _margin;
        private int _stepsRun;

        public MissionStepDriver(MissionExecutionContext execution, IMissionStep startingStep)
        {
            _execution = execution;
            _next = startingStep;
            _resume = null;
            _margin = 0f;
        }

        public bool IsComplete => _next == null;

        /// <summary>The step that will run next, or null when the mission is finished.</summary>
        public IMissionStep NextStep => _next;

        /// <summary>The mission this driver is walking, so the scheduler can read its day counter.</summary>
        public MissionContext State => _execution.State;

        internal MissionExecutionContext Execution => _execution;

        // A cross-mission interaction may terminate one participant while leaving the other
        // driver's ordinary chain intact. Keeping that mutation here avoids manufacturing a fake
        // terminal step merely to communicate the result back to the scheduler.
        internal void Complete()
        {
            _next = null;
            _resume = null;
            _followUps.Clear();
        }

        public void RunToCompletion()
        {
            while (_next != null)
            {
                AdvanceOneStep();
            }
        }

        /// <summary>
        /// Runs the next step and records where the chain goes afterwards. Returns false once the
        /// mission is finished.
        /// </summary>
        public bool AdvanceOneStep()
        {
            if (_next == null) return false;

            if (++_stepsRun > MaxStepsPerMission)
            {
                // Captured before _next is cleared: GameLog defers the lambda until it knows the
                // level is enabled, so reading _next inside it would report null.
                string stalledAt = _next.Description;
                MissionContext state = _execution.State;
                GameLog.Error(() =>
                    $"Mission step chain exceeded {MaxStepsPerMission} steps "
                    + $"({state.Order?.Mission?.MissionType}, day {state.DaysElapsed}, "
                    + $"next={stalledAt}); abandoning the chain to avoid an unbounded loop.");
                _next = null;
                return false;
            }

            IMissionStep step = _next;
            // This is the campaign-to-stage boundary. A doctrine edit, natural healing, or a
            // replacement completed since the last stage affects the next engagement, while the
            // participant set remains frozen for the battle currently being resolved.
            _execution.State.RefreshDutyReadyParticipants();
            MissionStepResult result = step.ExecuteMissionStep(_execution, _margin, _resume);

            if (result.Then != null)
            {
                _followUps.Push(result.Then);
            }

            if (result.Next != null)
            {
                _next = result.Next;
                _resume = result.Resume;
                _margin = result.Margin;
            }
            else if (_followUps.Count > 0)
            {
                // The chain finished, but a mandatory follow-up is owed - a raid or assassination
                // withdrawing after its engagement, whatever the engagement's outcome.
                _next = _followUps.Pop();
                _resume = null;
                _margin = 0f;
            }
            else
            {
                _next = null;
            }

            return _next != null;
        }
    }
}
