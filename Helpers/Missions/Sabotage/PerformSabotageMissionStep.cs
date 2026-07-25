using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Sabotage
{
    public class PerformSabotageMissionStep : IMissionStep
    {
        public string Description => "Sabotage Mission";

        public void ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep returnStep)
        {
            MissionContext context = execution.State;
            BaseSkill tactics = execution.Rules.Tactics;
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            // Unlike the stealth checks this stays anchored to the mission's target faction: it is a
            // Tactics check against the works being demolished, and those - like the Entrenchment
            // protecting them - belong to the target, not to whichever other factions happen to also
            // hold the region. Region-wide presence is already priced in by the stealth step that got
            // the force here. It does read deployed strength rather than raw Garrison, so a
            // PopulationIsMilitary horde (Tyranids, cults) whose army is its Population puts a real
            // guard force on its installations instead of the Log10(0) = -infinity that made every
            // sabotage attempt against one succeed for free.
            float difficulty = (float)(enemyFaction.Entrenchment * 0.5);
            difficulty += MissionStealthDifficulty.TroopMagnitude(enemyFaction.GetDeployedStrength());
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, difficulty);

            Order order = context.MissionSquads.First().Squad.CurrentOrders;

            context.AddLog($"Day {context.DaysElapsed}: Force plants explosives in {context.Order.Mission.RegionFaction.Region.Name}");
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);
            if(margin > 0)
            {
                context.Impact += margin;
            }

            if (context.OperatingDaysSpent)
            {
                // time to go home
                if (context.MustExfiltrate)
                {
                    new ExfiltrateMissionStep().ExecuteMissionStep(execution, 0.0f, this);
                }
                // otherwise we don't have to go anywhere, so just exit.
                return;
            }

            // Continue the SABOTAGE loop. This used to chain to ReconStealthMissionStep, whose success
            // branch runs PerformReconMissionStep - so after its first successful day a sabotage
            // mission silently turned into a recon mission: it stopped planting explosives and started
            // accruing recon Impact instead.
            new SabotageStealthMissionStep().ExecuteMissionStep(execution, marginOfSuccess, this);
        }
    }
}
