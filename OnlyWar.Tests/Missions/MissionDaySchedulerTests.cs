using System.Collections.Generic;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models.Missions;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Missions;

// The scheduler's contract, exercised with stub steps rather than real missions.
//
// This is deliberately not an integration test. The behaviour that matters - that two missions in the
// same region are part-way through the same day at the same time - is invisible to every existing
// test, because none of them set up two concurrent squad-bearing tactical missions in one turn. The
// whole suite passed unchanged when the day scheduler replaced sequential resolution, which proves
// only that the single-mission path still works: with one mission, day-by-day advancement walks the
// identical step order and consumes RNG identically. These tests cover the part that actually changed.
public class MissionDaySchedulerTests
{
    // The core guarantee. Under sequential resolution this trace would read A1 A2 A3 B1 B2 B3 - each
    // mission running its whole week before the next began. Interleaving is what lets one mission
    // observe another's effects mid-week.
    [Fact]
    public void TwoMissions_AdvanceDayByDayTogether()
    {
        List<string> trace = new();
        MissionStepDriver first = CreateDriver(new DailyStub("A", trace, days: 3));
        MissionStepDriver second = CreateDriver(new DailyStub("B", trace, days: 3));

        MissionDayScheduler.Run(new[] { first, second });

        Assert.Equal(
            new[] { "A:day1", "B:day1", "A:day2", "B:day2", "A:day3", "B:day3" },
            trace);
    }

    // Shaping resolves before Acting within each day, so a mission that moves the attention of a
    // region's occupants does so before the missions that have to cross that ground. The Acting
    // mission is registered FIRST so that list order alone cannot produce this result.
    [Fact]
    public void ShapingStepsResolveBeforeActingStepsEachDay()
    {
        List<string> trace = new();
        MissionStepDriver acting = CreateDriver(
            new DailyStub("act", trace, days: 2, phase: MissionStepPhase.Acting));
        MissionStepDriver shaping = CreateDriver(
            new DailyStub("shape", trace, days: 2, phase: MissionStepPhase.Shaping));

        MissionDayScheduler.Run(new[] { acting, shaping });

        Assert.Equal(
            new[] { "shape:day1", "act:day1", "shape:day2", "act:day2" },
            trace);
    }

    // A step that does not consume a day belongs to the day the previous step opened - the
    // detect/intercept/fight/escape chain all resolves on the day the scout was spotted. The naive
    // "advance until the day counter reaches today" rule would push that resolution into the
    // following morning, which is what IMissionStep.ConsumesDay exists to prevent.
    [Fact]
    public void SameDayResolutionStaysOnTheDayThatOpenedIt()
    {
        List<string> trace = new();
        MissionStepDriver withResolution = CreateDriver(
            new DailyStub("A", trace, days: 2, withSameDayResolution: true));
        MissionStepDriver plain = CreateDriver(new DailyStub("B", trace, days: 2));

        MissionDayScheduler.Run(new[] { withResolution, plain });

        Assert.Equal(
            new[]
            {
                "A:day1", "A:resolve1", "B:day1",
                "A:day2", "A:resolve2", "B:day2"
            },
            trace);
    }

    // A mission that finishes early must not hold up or distort the others.
    [Fact]
    public void MissionsWithDifferentLengths_EachStopAtTheirOwnEnd()
    {
        List<string> trace = new();
        MissionStepDriver shortMission = CreateDriver(new DailyStub("short", trace, days: 1));
        MissionStepDriver longMission = CreateDriver(new DailyStub("long", trace, days: 3));

        MissionDayScheduler.Run(new[] { shortMission, longMission });

        Assert.Equal(
            new[] { "short:day1", "long:day1", "long:day2", "long:day3" },
            trace);
        Assert.True(shortMission.IsComplete);
        Assert.True(longMission.IsComplete);
    }

    private static MissionStepDriver CreateDriver(IMissionStep startingStep)
    {
        // Order is unused by the stubs, and MissionContext tolerates a null one (every read of it is
        // null-conditional), which keeps this a test of the scheduler rather than of world building.
        MissionContext context = new(null, new List<BattleSquad>(), new List<BattleSquad>());
        return new MissionStepDriver(
            TestExecutionContextFactory.CreateMission(context, new FixedRNG()),
            startingStep);
    }

    // Spends a day, records it, and repeats until its budget runs out - the shape of every
    // day-consuming step (the three stealth approaches, infiltrate/exfiltrate, the feint).
    private sealed class DailyStub : IMissionStep
    {
        private readonly string _name;
        private readonly List<string> _trace;
        private readonly int _days;
        private readonly MissionStepPhase _phase;
        private readonly bool _withSameDayResolution;

        internal DailyStub(
            string name,
            List<string> trace,
            int days,
            MissionStepPhase phase = MissionStepPhase.Acting,
            bool withSameDayResolution = false)
        {
            _name = name;
            _trace = trace;
            _days = days;
            _phase = phase;
            _withSameDayResolution = withSameDayResolution;
        }

        public string Description => _name;
        public MissionStepPhase Phase => _phase;
        public bool ConsumesDay => true;

        public MissionStepResult ExecuteMissionStep(
            MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
        {
            MissionContext context = execution.State;
            context.DaysElapsed++;
            _trace.Add($"{_name}:day{context.DaysElapsed}");
            if (_withSameDayResolution)
            {
                return MissionStepResult.Continue(
                    new SameDayStub(_name, _trace, this, _days));
            }
            return context.DaysElapsed >= _days
                ? MissionStepResult.Complete
                : MissionStepResult.Continue(this);
        }
    }

    // Resolves inside the day the previous step opened, then loops back - the shape of
    // PerformReconMissionStep and the detection chain.
    private sealed class SameDayStub : IMissionStep
    {
        private readonly string _name;
        private readonly List<string> _trace;
        private readonly IMissionStep _loop;
        private readonly int _days;

        internal SameDayStub(string name, List<string> trace, IMissionStep loop, int days)
        {
            _name = name;
            _trace = trace;
            _loop = loop;
            _days = days;
        }

        public string Description => $"{_name} resolution";
        public bool ConsumesDay => false;

        public MissionStepResult ExecuteMissionStep(
            MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
        {
            MissionContext context = execution.State;
            _trace.Add($"{_name}:resolve{context.DaysElapsed}");
            return context.DaysElapsed >= _days
                ? MissionStepResult.Complete
                : MissionStepResult.Continue(_loop);
        }
    }
}
