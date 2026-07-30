using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Assassinate
{
    public class AssassinateStealthMissionStep : IMissionStep
    {
        public string Description { get { return "Assassinate Stealth"; } }

        public bool ConsumesDay => true;

        public AssassinateStealthMissionStep() { }

        public MissionStepResult ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
        {
            MissionContext context = execution.State;
            // This step is the assassination loop's re-entry point - DetectedMissionStep is handed
            // `this` as its returnStep - so the day budget has to be consulted here, as in
            // ReconStealthMissionStep and SabotageStealthMissionStep. Without it the loop has no
            // upper bound at all: a force that is detected but not intercepted (the region spots it
            // yet fields no ScoutPatrol) returns straight here and rolls stealth again forever. That
            // path was unreachable only because the difficulty below used to be -infinity, so the
            // stealth check could never fail; guarding the log makes it reachable.
            if (context.OperatingDaysSpent)
            {
                return context.MustExfiltrate
                    ? MissionStepResult.Continue(new ExfiltrateMissionStep())
                    : MissionStepResult.Complete;
            }

            // negative mod for size of enemy force
            // mod for terrain
            // mod for enemy recon focus
            // mod for equipment
            BaseSkill stealth = execution.Rules.Stealth;
            Region region = context.Order.Mission.RegionFaction.Region;
            Faction assassin = context.MissionSquads.FirstOrDefault()?.Squad.Faction;
            int headcount = context.MissionSquads.Sum(s => s.AbleSoldiers.Count);
            // Getting close to the target unseen is contested by everyone watching the ground, not
            // just the faction the target belongs to, so this uses the same aggregated search-effort
            // model as ReconStealthMissionStep - and with it that model's log10(1 + x) shape, so a
            // region held by a zero-Garrison horde can no longer produce Log(0) = -infinity, which
            // used to hand every assassin an infinite margin and a free approach.
            float difficulty = MissionStealthDifficulty
                .Calculate(region, headcount, assassin).Total
                // Aggression's EXPOSURE axis: a cautious approach keeps its distance and is harder to
                // spot. The other half of the trade is the shot itself in
                // PerformAssassinationMissionStep, which moves the opposite way.
                + MissionAggressionModifiers.ExposureDifficulty(context.Order.LevelOfAggression);
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);

            context.DaysElapsed++;
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);
            return margin > 0.0f
                ? MissionStepResult.Continue(new PerformAssassinationMissionStep(), margin, this)
                : MissionStepResult.Continue(new DetectedMissionStep(), margin, this);
        }
    }
}
