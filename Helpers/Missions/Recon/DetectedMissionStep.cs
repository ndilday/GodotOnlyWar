using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Recon
{
    public class DetectedMissionStep : IMissionStep
    {
        // Interception strength required, as a multiple of the intruding force's battle value: parity at
        // best, rising with how badly the intruder was seen. A marginal detection (about -0.5 sigma)
        // wants roughly 1.25x the intruder; a thoroughly blown one (-3) wants 2.5x.
        private const double InterceptionBaseMultiple = 1.0;
        private const double InterceptionMultiplePerSigma = 0.5;

        public string Description { get { return "Detected"; } }

        public DetectedMissionStep(){}

        private static long AbleBattleValue(IEnumerable<BattleSquad> squads) =>
            squads
                .SelectMany(squad => squad.AbleSoldiers)
                .Sum(soldier => (long)soldier.Soldier.Template.BattleValue);

        private static long AbleBattleValue(Squad squad) =>
            squad.Members
                .Where(member => member.IsCombatEffective)
                .Sum(member => (long)member.Template.BattleValue);

        public MissionStepResult ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
        {
            MissionContext context = execution.State;

            // The spotter (resolved by the detection step via Region.SelectSpotter) is the faction that
            // actually caught the scout, and it may not be the mission's anchor RegionFaction when the
            // region holds several enemy factions. Fall back to the mission's target for flows that do
            // not resolve a spotter (special missions carry a concrete target of their own).
            RegionFaction spotter = context.Spotter ?? context.Order.Mission.RegionFaction;
            context.Spotter = spotter;

            int intruderCount = context.MissionSquads.Sum(
                squad => squad.AbleSoldiers.Count);
            if (intruderCount == 0)
            {
                bool lostWhileExfiltrating = resumeStep is ExfiltrateMissionStep;
                if (lostWhileExfiltrating)
                {
                    context.ForceLostContact = true;
                }
                else
                {
                    context.ObjectiveAborted = true;
                }
                context.OpposingSquads = [];
                context.AddLog(
                    $"Day {context.DaysElapsed}: Force was detected in {spotter.Region.Name}, "
                    + "but no combat-capable mission force remained; the mission ended.");
                GameLog.Trace(() =>
                    $"Detected {DescribeRegion(context)} day {context.DaysElapsed}: "
                    + "intruders=0; mission force combat-ineffective, "
                    + (lostWhileExfiltrating ? "lost during exfiltration" : "objective aborted"));
                return MissionStepResult.Complete;
            }

            // How much force it would take to deal with this intrusion: a multiple of the intruding
            // force, growing with how badly the intruder blew its stealth check (marginOfSuccess is <= 0
            // in this branch). Sized in battle value rather than squad count, because a squad is not a
            // unit of strength - the old rule generated one conjured squad per intruding squad plus one
            // per sigma of failure, which meant a region's actual screen had no bearing at all on what
            // showed up.
            long intruderBattleValue = AbleBattleValue(context.MissionSquads);
            double requiredMultiple = InterceptionBaseMultiple
                + Math.Abs(marginOfSuccess) * InterceptionMultiplePerSigma;
            long requiredBattleValue = (long)Math.Round(intruderBattleValue * requiredMultiple);

            // Only forces actually out looking can intercept: squads on a Patrol or Recon order in the
            // spotter's region. Nothing is conjured. A region with sensors but nobody sweeping knows
            // perfectly well that there are enemies out there and is too busy to do anything about it.
            List<Squad> screen = spotter.LandedSquads
                .Where(squad => squad.CurrentOrders?.Mission.MissionType == MissionType.Patrol
                    || squad.CurrentOrders?.Mission.MissionType == MissionType.Recon)
                .Where(squad => squad.Members.Any(member => member.IsCombatEffective))
                // Largest first, so the screen commits the fewest squads that will do the job and the
                // rest carry on screening.
                .OrderByDescending(AbleBattleValue)
                .ToList();
            long screenBattleValue = screen.Sum(AbleBattleValue);

            // Below parity with the intruder the screen declines to engage rather than feeding itself in
            // piecemeal. The intrusion is still DETECTED - that already happened - it simply goes
            // uncontested.
            if (screen.Count == 0 || screenBattleValue < intruderBattleValue)
            {
                context.AddLog(
                    $"Day {context.DaysElapsed}: Force was spotted in {spotter.Region.Name}, but no "
                    + "force strong enough to engage it is in position; the mission presses on.");
                GameLog.Trace(() =>
                    $"Detected {DescribeRegion(context)} day {context.DaysElapsed}: "
                    + $"screenBV={screenBattleValue} < intruderBV={intruderBattleValue}; "
                    + "screen declines to engage, recon uncontested");
                return resumeStep == null
                    ? MissionStepResult.Complete
                    : MissionStepResult.Continue(resumeStep, marginOfSuccess, resumeStep);
            }

            // Commit squads until the requirement is met. Reaching parity but falling short of the ideal
            // still engages - with less than it wanted, which is the cost of a thin screen.
            List<BattleSquad> interceptors = new();
            long committedBattleValue = 0;
            foreach (Squad squad in screen)
            {
                // A modded or legacy combatant may carry zero BattleValue. Still commit one real
                // squad rather than letting a zero requirement produce an empty "interception".
                if (interceptors.Count > 0
                    && committedBattleValue >= requiredBattleValue) break;
                interceptors.Add(new BattleSquad(squad.Faction?.IsPlayerFaction == true, squad));
                committedBattleValue += AbleBattleValue(squad);
            }
            context.OpposingSquads = interceptors;

            context.AddLog(
                $"Day {context.DaysElapsed}: Force was detected and intercepted in {spotter.Region.Name}.");
            GameLog.Debug(() =>
                $"Interception {DescribeRegion(context)} day {context.DaysElapsed}: "
                + $"intruderBV={intruderBattleValue}, margin={marginOfSuccess:F2}, "
                + $"requiredBV={requiredBattleValue} (x{requiredMultiple:F2}), screenBV={screenBattleValue} "
                + $"-> committed {interceptors.Count} squad(s), {committedBattleValue} BV");

            // The scout's leader now tries to slip past the responders (a Tactics contest); the more
            // defenders were scrambled, the harder that is. Difficulty reads the ACTUAL generated
            // OpFor — the old code evaluated this from context.OpposingSquads *before* it was
            // populated, so it computed Log(0) = -infinity and the scout trivially won every contest.
            BaseSkill tactics = execution.Rules.Tactics;
            int opForSize = Math.Max(1, context.OpposingSquads.Sum(s => s.AbleSoldiers.Count));
            float difficulty = 10.0f + (float)Math.Log(opForSize, 10);
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, difficulty);
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);
            string interceptorFaction =
                spotter.PlanetFaction?.Faction?.Name ?? "an unidentified force";
            GameLog.Trace(() =>
                $"Detected {DescribeRegion(context)} day {context.DaysElapsed}: "
                + $"intercepted by {context.OpposingSquads.Sum(s => s.AbleSoldiers.Count)} "
                + $"{interceptorFaction} ({context.OpposingSquads.Count} squads), "
                + $"tacticsMargin={margin:F2} -> {(margin > 0 ? "outmaneuvered them (cross-detection)" : "AMBUSHED")}");
            return margin > 0.0f
                ? MissionStepResult.Continue(new CrossDetectionMissionStep(), margin, resumeStep)
                : MissionStepResult.Continue(new AmbushedMissionStep(), margin, resumeStep);
        }

        private static string DescribeRegion(MissionContext context)
        {
            // Reflect the spotter (the faction that intercepts) when one was resolved, falling back to
            // the mission's anchor faction for flows that never set a spotter.
            RegionFaction anchor = context.Spotter ?? context.Order.Mission.RegionFaction;
            return $"{anchor.Region.Planet.Name}/{anchor.Region.Name}/{anchor.PlanetFaction.Faction.Name}";
        }
    }
}
