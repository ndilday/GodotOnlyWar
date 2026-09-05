using OnlyWar.Models.Missions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Missions
{
    /// <summary>
    /// Resolves every active mission one campaign day at a time, instead of running each mission from
    /// start to finish before the next one begins.
    /// </summary>
    /// <remarks>
    /// This is the point of the whole exercise (OnlyWar_TDD.md §6.4). Previously a
    /// mission's entire week resolved inside a single call, so no two missions were ever part-way
    /// through the same day and every cross-mission interaction had to be expressed as phase ordering
    /// around whole missions. With the days owned here, missions in the same region observe each
    /// other's effects as they happen: a battle that breaks a patrol screen on day 2 makes day 3's
    /// stealth checks easier for everyone still operating there, with nothing coordinating it.
    ///
    /// Within a day, every mission's Shaping steps resolve before any mission's Acting steps, so a
    /// mission that moves the attention of a region's occupants does so before the missions that have
    /// to cross that ground. The ordering is keyed off <see cref="IMissionStep.Phase"/> and never off
    /// MissionType - see <see cref="MissionStepPhase"/> for why that distinction matters.
    /// </remarks>
    public static class MissionDayScheduler
    {
        // A mission runs at most a week, plus the grace an exfiltrating force gets to break contact.
        // Past this the per-step guards (MissionContext.OperatingDaysSpent, the exfiltration grace
        // check) have already ended every legitimate mission.
        private const int MaxDay =
            MissionContext.MissionDurationDays + MissionContext.ExfiltrationGraceDays;

        /// <param name="onDayStart">
        /// Called with the day number before anything resolves on it. This is where per-day world state
        /// is reset - notably <see cref="Models.Planets.RegionFaction.CommittedAttention"/>, so that
        /// attention a feint drew yesterday does not shelter an infiltrator today. Passed in rather than
        /// reached for directly because the scheduler has no business knowing about planets.
        /// </param>
        /// <param name="onDayEnd">
        /// Called with the day number once every mission has resolved everything it does on that
        /// day. This is where between-days recovery belongs - notably the Astartes daily healing
        /// pass (Design/Reference/CasualtyRealism.md §2.5), which must land after the day's fighting
        /// so a brother scratched on day 2 starts day 3 clean.
        ///
        /// Deliberately a scheduler-level hook and NOT a per-driver one: one order can fan out
        /// into several independent single-squad drivers (MissionForceMode.IndependentSquads), so
        /// anything hung off a driver would run several times a day over overlapping men. Invoked
        /// exactly once per day, before the early-exit check, so the last day of fighting still
        /// gets its pass.
        /// </param>
        public static void Run(
            IReadOnlyList<MissionStepDriver> missions,
            Action<int> onDayStart = null,
            Action<IReadOnlyList<MissionStepDriver>, int> onActingDayStart = null,
            Action<int> onDayEnd = null)
        {
            if (missions == null || missions.Count == 0) return;

            for (int day = 1; day <= MaxDay; day++)
            {
                onDayStart?.Invoke(day);
                AdvancePhase(missions, day, MissionStepPhase.Shaping);
                // Cross-mission acting interactions belong after every shaping effect but before
                // any ordinary acting step. Reciprocal assaults use this seam to replace two
                // independent attacks with one shared meeting engagement.
                onActingDayStart?.Invoke(missions, day);
                AdvancePhase(missions, day, MissionStepPhase.Acting);
                onDayEnd?.Invoke(day);
                if (missions.All(mission => mission.IsComplete)) break;
            }

            // Safety net, not the normal path. A chain that somehow stops advancing its day counter
            // would otherwise be abandoned part-way through, leaving a mission with no outcome and
            // squads stranded mid-step. Finishing it here costs the interleaving for that tail only,
            // and by this point every other mission has ended anyway. MissionStepDriver's own step
            // cap bounds this.
            foreach (MissionStepDriver mission in missions)
            {
                mission.RunToCompletion();
            }
        }

        private static void AdvancePhase(
            IReadOnlyList<MissionStepDriver> missions,
            int day,
            MissionStepPhase phase)
        {
            foreach (MissionStepDriver mission in missions)
            {
                if (mission.IsComplete) continue;
                // The phase of the step the mission is ABOUT to run decides which pass it belongs to.
                // A chain can contain steps of both phases within one day; bucketing on the entry
                // step is what keeps the pass ordering meaningful without splitting a day's chain in
                // half.
                if (mission.NextStep.Phase != phase) continue;
                AdvanceThroughDay(mission, day);
            }
        }

        private static void AdvanceThroughDay(MissionStepDriver mission, int day)
        {
            // Spend the step that OPENS this day. The increment marks the start of a day's activity,
            // not its end, so this is the step whose work belongs to `day`.
            while (!mission.IsComplete && mission.State.DaysElapsed < day)
            {
                mission.AdvanceOneStep();
            }

            // Then resolve the rest of this day: everything up to, but not including, the step that
            // would open the next one. This is what keeps a detection and its interception, battle,
            // and escape on the same day they were triggered, rather than smearing them across the
            // following morning.
            while (!mission.IsComplete
                && mission.State.DaysElapsed == day
                && !mission.NextStep.ConsumesDay)
            {
                mission.AdvanceOneStep();
            }
        }
    }
}
