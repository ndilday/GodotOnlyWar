﻿using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
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
            BaseSkill tactics = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Tactics");
            // adjust for size of detecting force
            float difficulty = 10.0f;
            difficulty += (float)Math.Log(context.OpposingSquads.Sum(s => s.AbleSoldiers.Count), 10);
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, difficulty);
            // build OpFor, size increases the lower the MoS, and pushes engagement range in favor of the OpFor
            int numberOfOpposingSquads = context.MissionSquads.Count - (ushort)marginOfSuccess;
            // any fractional value of margin of Success is treated as the probability of an additional squad being added.
            float fraction = Math.Abs(marginOfSuccess - (ushort)marginOfSuccess);
            if (RNG.GetLinearDouble() < fraction)
            {
                numberOfOpposingSquads++;
            }

            // shouldn't all be the same squad type
            // a flexible, but verbose method would be to define a table in the game rules that maps some concept of "situation" and faction ID to "lottery balls". 
            // Then, here we would total the number of qualifying units lottery balls, and roll an int against that to generate a reasonable mix of units.
            // for now, get all squads of the OpFor faction and select one for each opFor squad needed
            var request = new ForceGenerationRequest
            {
                Faction = context.Order.Mission.RegionFaction.PlanetFaction.Faction,
                Profile = ForceCompositionProfile.ScoutPatrol,
                Tier = numberOfOpposingSquads
            };
            context.OpposingSquads = ForceGenerator.GenerateForce(request).Select(s => new BattleSquad(false, s)).ToList();

            float margin = missionTest.RunMissionCheck(context.MissionSquads);
            if (margin > 0.0f)
            {
                new CrossDetectionMissionStep().ExecuteMissionStep(context, margin, returnStep);
            }
            else
            {
                new AmbushedMissionStep().ExecuteMissionStep(context, margin, returnStep);
            }
        }
    }
}
