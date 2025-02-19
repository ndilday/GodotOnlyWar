using OnlyWar.Builders;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Recon
{
    public class DetectedMissionStep : ITestMissionStep
    {
        private readonly IMissionCheck _missionTest;

        public string Description { get { return "Detected"; } }
        public IMissionCheck MissionTest { get; }
        public IMissionStep StepIfSuccess { get; }
        public IMissionStep StepIfFailure { get; }

        public DetectedMissionStep()
        {
            BaseSkill perception = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Tactics");
            _missionTest = new SquadMissionTest(perception, 10.0f);
            StepIfSuccess = new CrossDetectionMissionStep();
            StepIfFailure = new AmbushedMissionStep();
        }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            // build OpFor, size increases the lower the MoS, and pushes engagement range in favor of the OpFor
            int numberOfOpposingSquads = context.PlayerSquads.Count - (ushort)marginOfSuccess;
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
            Faction opposingFaction = context.Region.RegionFactionMap.Values.First(rf => !rf.PlanetFaction.Faction.IsPlayerFaction && !rf.PlanetFaction.Faction.IsDefaultFaction).PlanetFaction.Faction;
            IEnumerable<SquadTemplate> scoutSquadTemplates = opposingFaction.SquadTemplates.Values.Where(st => (st.SquadType & SquadTypes.Scout) > 0);
            int count = scoutSquadTemplates.Count();
            for (int i = 0; i < numberOfOpposingSquads; i++)
            {
                SquadTemplate squadTemplate = scoutSquadTemplates.ElementAt(RNG.GetIntBelowMax(0, count));
                Squad squad = SquadFactory.GenerateSquad(squadTemplate, $"{opposingFaction.Name} {context.Region.Name} Recon Squad {i}");
                context.OpposingForces.Clear();
                context.OpposingForces.Add(squad);
            }

            float margin = _missionTest.RunMissionCheck(context.PlayerSquads);
            if (margin > 0.0f)
            {
                StepIfSuccess.ExecuteMissionStep(context, margin, returnStep);
            }
            else
            {
                StepIfFailure.ExecuteMissionStep(context, margin, returnStep);
            }
        }
    }
}
