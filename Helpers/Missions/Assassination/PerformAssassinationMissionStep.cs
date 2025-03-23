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
            BaseSkill tactics = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Tactics");
            // size 1: Prime
            // size 2: Broodlord
            // size 3: Hive Tyrant
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            float difficulty = enemyFaction.Entrenchment + enemyFaction.Detection + (float)Math.Log10(enemyFaction.Garrison);
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, difficulty);
            float margin = missionTest.RunMissionCheck(context.PlayerSquads);
            // my current data design doesn't handle HQ+Bodyguard in a single squad very well, so for now, I should come up with a way to associate each HQ with a particular separate bodyguard squad
            GenerateTargetSquad(context, enemyFaction, margin <= 0);

            context.Log.Add($"Day {context.DaysElapsed}: Force has located the assassination target");

            if (context.DaysElapsed >= 6)
            {
                // time to go home
                if (context.Order.Mission.RegionFaction.Region != context.PlayerSquads.First().Squad.CurrentRegion)
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

        private static void GenerateTargetSquad(MissionContext context, RegionFaction enemyFaction, bool addBodyguard)
        {
            var sortedHqSquads = enemyFaction.PlanetFaction.Faction.SquadTemplates.Values
                                .Where(st => (st.SquadType & SquadTypes.HQ) == SquadTypes.HQ)
                                .OrderBy(st => st.BattleValue)
                                .ToList();
            int index = Math.Min(context.Order.Mission.MissionSize, sortedHqSquads.Count) - 1;
            SquadTemplate targetSquadTemplate = sortedHqSquads[index];
            Squad squad = SquadFactory.GenerateSquad(targetSquadTemplate, $"{enemyFaction.PlanetFaction.Faction.Name} {context.Order.Mission.RegionFaction.Region.Name} HQ Squad");
            context.OpposingForces.Clear();
            context.OpposingForces.Add(new BattleSquad(false, squad));
            if (addBodyguard && targetSquadTemplate.BodyguardSquadTemplate != null)
            {
                squad = SquadFactory.GenerateSquad(targetSquadTemplate.BodyguardSquadTemplate, $"{enemyFaction.PlanetFaction.Faction.Name} {context.Order.Mission.RegionFaction.Region.Name} Bodyguard Squad");
                context.OpposingForces.Add(new BattleSquad(false, squad));
            }
        }
    }
}
