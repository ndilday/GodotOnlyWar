using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fortifications;
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

        public MissionStepResult ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
        {
            MissionContext context = execution.State;
            BaseSkill tactics = execution.Rules.Tactics;
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            // The guard strength stays anchored to the mission's target faction - region-wide
            // presence is already priced in by the stealth step that got the force here - but the
            // Entrenchment protecting the works is the side's shared position, since allies holding
            // a region man one set of works between them (RegionDefenses). Deployed strength rather
            // than raw Garrison, so a PopulationIsMilitary horde (Tyranids, cults) whose army is its
            // Population puts a real guard force on its installations.
            //
            // Like PerformAssassinationMissionStep it borrows only Magnitude's log10(1 + x) shape and
            // is deliberately NOT on MissionStealthDifficulty's search-effort model: the question is
            // "how well guarded are these works", not "who in this region is looking for me", so the
            // ambient cap and the patrol/static split would both be wrong here. The shifted form is
            // what keeps an unguarded installation at 0 instead of the Log10(0) = -infinity that made
            // every sabotage attempt against one succeed for free.
            float difficulty = (float)(
                RegionDefenses.GetShared(enemyFaction, DefenseType.Entrenchment) * 0.5);
            difficulty += MissionStealthDifficulty.Magnitude(enemyFaction.GetDeployedStrength());
            // Aggression's EFFECT axis: how thoroughly the charges get placed. A force keeping its
            // distance plants less.
            difficulty += MissionAggressionModifiers.EffectDifficulty(context.Order.LevelOfAggression);
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
                // time to go home; otherwise we don't have to go anywhere, so just exit.
                return context.MustExfiltrate
                    ? MissionStepResult.Continue(new ExfiltrateMissionStep(), 0.0f, this)
                    : MissionStepResult.Complete;
            }

            // Continue the SABOTAGE loop. This used to chain to ReconStealthMissionStep, whose success
            // branch runs PerformReconMissionStep - so after its first successful day a sabotage
            // mission silently turned into a recon mission: it stopped planting explosives and started
            // accruing recon Impact instead.
            return MissionStepResult.Continue(new SabotageStealthMissionStep(), marginOfSuccess, this);
        }
    }
}
