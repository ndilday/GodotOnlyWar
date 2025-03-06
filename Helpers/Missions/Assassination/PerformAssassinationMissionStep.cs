using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models;
using System;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Assassination
{
    public class PerformAssassinationMissionStep : IMissionStep
    {
        public string Description => "Assassination Mission";

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            BaseSkill tactics = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Tactics");
            RegionFaction enemyFaction = context.Region.RegionFactionMap.Values.First(rf => !rf.PlanetFaction.Faction.IsPlayerFaction && !rf.PlanetFaction.Faction.IsDefaultFaction);
            float difficulty = enemyFaction.Entrenchment;
            difficulty += (float)Math.Log10(enemyFaction.Garrison);
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, difficulty);

            SabotageOrder order = (SabotageOrder)context.PlayerSquads.First().Squad.CurrentOrders;

            context.Log.Add($"Day {context.DaysElapsed}: Force plants explosives in {context.Region.Name}");
            float margin = missionTest.RunMissionCheck(context.PlayerSquads);
            if (margin > 0)
            {
                context.Impact += margin;
            }

            if (context.DaysElapsed >= 6)
            {
                // time to go home
                if (context.Region != context.PlayerSquads.First().Squad.CurrentRegion)
                {
                    new ExfiltrateMissionStep().ExecuteMissionStep(context, 0.0f, this);
                }
                else if (context.DaysElapsed >= 7)
                {
                    //we don't have to go anywhere so just exit.
                    return;
                }
            }
            else
            {
                new ReconStealthMissionStep().ExecuteMissionStep(context, marginOfSuccess, this);
            }
        }
    }
}
