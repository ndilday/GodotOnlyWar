using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models;
using System;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;

namespace OnlyWar.Helpers.Missions.Assassination
{
    public class PerformAssassinationMissionStep : IMissionStep
    {
        public string Description => "Assassination Mission";

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            AssassinationOrder assassinationOrder = (AssassinationOrder)context.PlayerSquads.First().Squad.CurrentOrders;

            // size 1: Prime
            // size 2: Broodlord
            // size 3: Hive Tyrant
            RegionFaction enemyFaction = context.Region.RegionFactionMap.Values.First(rf => !rf.PlanetFaction.Faction.IsPlayerFaction && !rf.PlanetFaction.Faction.IsDefaultFaction);
            var sortedHqSquads = enemyFaction.PlanetFaction.Faction.SquadTemplates.Values
                .Where(st => (st.SquadType & SquadTypes.HQ) > 0)
                .OrderBy(st => st.BattleValue)
                .ToList();
            int index = Math.Min(assassinationOrder.TargetSize, sortedHqSquads.Count) - 1;
            SquadTemplate targetSquadTemplate = sortedHqSquads[index];
            Squad squad = SquadFactory.GenerateSquad(targetSquadTemplate, $"{enemyFaction.PlanetFaction.Faction.Name} {context.Region.Name} HQ Squad");
            context.OpposingForces.Clear();
            context.OpposingForces.Add(new BattleSquad(false, squad));

            BaseSkill tactics = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Tactics");
            
            float difficulty = enemyFaction.Entrenchment;
            difficulty += (float)Math.Log10(enemyFaction.Garrison);
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, difficulty);


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
