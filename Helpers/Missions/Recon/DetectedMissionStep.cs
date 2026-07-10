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
        public string Description { get { return "Detected"; } }

        public DetectedMissionStep(){}

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            // Build the intercepting OpFor. Its size grows the worse the scout's stealth margin was
            // (a badly-blown infiltration is met by more defenders). marginOfSuccess is <= 0 here —
            // this is the detected branch — so subtracting its truncated value ADDS squads. The cast
            // MUST be to a signed type: the old (ushort) cast turned a negative margin (e.g. -1.7)
            // into ~65535 and underflowed the count to a garbage negative, so GenerateScoutPatrol's
            // `for (i = tier; i > 0; i--)` produced zero interceptors and no engagement ever happened.
            int numberOfOpposingSquads = context.MissionSquads.Count - (short)marginOfSuccess;
            // any fractional value of margin of Success is treated as the probability of an additional squad being added.
            float fraction = Math.Abs(marginOfSuccess - (short)marginOfSuccess);
            if (RNG.GetLinearDouble() < fraction)
            {
                numberOfOpposingSquads++;
            }
            // A detected intrusion always draws at least one responding squad.
            numberOfOpposingSquads = Math.Max(1, numberOfOpposingSquads);

            // The spotter (resolved by the detection step via Region.SelectSpotter) is the faction that
            // actually caught the scout, and it may not be the mission's anchor RegionFaction when the
            // region holds several enemy factions. Fall back to the mission's target for flows that do
            // not resolve a spotter (special missions carry a concrete target of their own).
            RegionFaction spotter = context.Spotter ?? context.Order.Mission.RegionFaction;

            // shouldn't all be the same squad type
            // a flexible, but verbose method would be to define a table in the game rules that maps some concept of "situation" and faction ID to "lottery balls".
            // Then, here we would total the number of qualifying units lottery balls, and roll an int against that to generate a reasonable mix of units.
            // for now, get all squads of the OpFor faction and select one for each opFor squad needed
            var request = new ForceGenerationRequest
            {
                Faction = spotter.PlanetFaction.Faction,
                Profile = ForceCompositionProfile.ScoutPatrol,
                Tier = numberOfOpposingSquads
            };
            context.OpposingSquads = ForceGenerator.GenerateForce(request).Select(s => new BattleSquad(false, s)).ToList();

            // No force may actually materialize to intercept the scout: the region can detect the
            // intrusion (a listening post over a bare garrison) yet have no squads to scramble, or
            // the OpFor size can round to zero, or the faction may have no ScoutPatrol composition.
            // With no OpFor there is nothing to engage, and every downstream battle step assumes a
            // non-empty OpposingSquads (First()/Average() over it) and would throw. Treat the recon
            // as uncontested and continue the mission rather than fighting a phantom force.
            if (context.OpposingSquads.Count == 0)
            {
                context.Log.Add(
                    $"Day {context.DaysElapsed}: Detected in {spotter.Region.Name}, "
                    + "but no enemy force intercepts; the force presses on.");
                GameLog.Trace(() =>
                    $"Detected {DescribeRegion(context)} day {context.DaysElapsed}: "
                    + $"intercept force requested (tier={numberOfOpposingSquads}) but none materialized "
                    + $"({spotter.PlanetFaction.Faction.Name} fielded no ScoutPatrol); "
                    + "recon uncontested, presses on");
                returnStep?.ExecuteMissionStep(context, marginOfSuccess, returnStep);
                return;
            }

            // The scout's leader now tries to slip past the responders (a Tactics contest); the more
            // defenders were scrambled, the harder that is. Difficulty reads the ACTUAL generated
            // OpFor — the old code evaluated this from context.OpposingSquads *before* it was
            // populated, so it computed Log(0) = -infinity and the scout trivially won every contest.
            BaseSkill tactics = GameDataSingleton.Instance.GameRulesData.Skills.Tactics;
            int opForSize = Math.Max(1, context.OpposingSquads.Sum(s => s.AbleSoldiers.Count));
            float difficulty = 10.0f + (float)Math.Log(opForSize, 10);
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, difficulty);
            float margin = missionTest.RunMissionCheck(context.MissionSquads);
            GameLog.Trace(() =>
                $"Detected {DescribeRegion(context)} day {context.DaysElapsed}: "
                + $"intercepted by {context.OpposingSquads.Sum(s => s.AbleSoldiers.Count)} "
                + $"{context.OpposingSquads.First().Squad.Faction.Name} ({context.OpposingSquads.Count} squads), "
                + $"tacticsMargin={margin:F2} -> {(margin > 0 ? "outmaneuvered them (cross-detection)" : "AMBUSHED")}");
            if (margin > 0.0f)
            {
                new CrossDetectionMissionStep().ExecuteMissionStep(context, margin, returnStep);
            }
            else
            {
                new AmbushedMissionStep().ExecuteMissionStep(context, margin, returnStep);
            }
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
