using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Builders
{
    public enum ForceCompositionProfile
    {
        Garrison,       // Standard infantry-heavy force for defense
        AssaultForce,   // A balanced offensive army
        AmbushForce,    // A force composition suitable for ambushes (e.g., more long-range firepower)
        ScoutPatrol,    // Quick reaction force of scout units
        SpecialHQTarget // A specific HQ unit and its bodyguard
    }

    public struct ForceGenerationRequest
    {
        public Faction Faction;
        public int TargetBattleValue;
        public ForceCompositionProfile Profile;
        public int Tier; // Optional, for things like Assassination target level
    }

    public static class ForceGenerator
    {
        public static List<Squad> GenerateForce(ForceGenerationRequest request)
        {
            switch (request.Profile)
            {
                case ForceCompositionProfile.SpecialHQTarget:
                    return GenerateHqEncounter(request);

                case ForceCompositionProfile.ScoutPatrol:
                    return GenerateScoutPatrol(request);

                case ForceCompositionProfile.Garrison:
                case ForceCompositionProfile.AssaultForce:
                case ForceCompositionProfile.AmbushForce:
                default:
                    return GenerateGenericForce(request);
            }
        }

        private static List<Squad> GenerateGenericForce(ForceGenerationRequest request)
        {
            var generatedSquads = new List<Squad>();
            var availableTemplates = request.Faction.SquadTemplates.Values
                                            .Where(st => (st.SquadType & SquadTypes.HQ) == 0) // Exclude HQs from generic forces
                                            .OrderByDescending(st => st.BattleValue)
                                            .ToList();

            if (!availableTemplates.Any()) return new List<Squad>();

            int remainingValue = request.TargetBattleValue;

            while (remainingValue > 0)
            {
                var affordableTemplate = availableTemplates.FirstOrDefault(t => t.BattleValue <= remainingValue);
                if (affordableTemplate == null)
                {
                    // No single squad can fit the remaining budget, so we stop.
                    break;
                }
                generatedSquads.Add(SquadFactory.GenerateSquad(affordableTemplate));
                remainingValue -= affordableTemplate.BattleValue;
            }

            return generatedSquads;
        }

        private static List<Squad> GenerateHqEncounter(ForceGenerationRequest request)
        {
            var opposingForces = new List<Squad>();
            var sortedHqSquads = request.Faction.SquadTemplates.Values
                                .Where(st => (st.SquadType & SquadTypes.HQ) == SquadTypes.HQ)
                                .OrderBy(st => st.BattleValue)
                                .ToList();

            if (!sortedHqSquads.Any()) return opposingForces;

            int index = Math.Clamp(request.Tier - 1, 0, sortedHqSquads.Count - 1);
            SquadTemplate targetSquadTemplate = sortedHqSquads[index];
            Squad hqSquad = SquadFactory.GenerateSquad(targetSquadTemplate);
            opposingForces.Add(hqSquad);

            // Use the BattleValue to determine if a bodyguard should be added
            if (request.TargetBattleValue <= 0 && targetSquadTemplate.BodyguardSquadTemplate != null)
            {
                Squad bodyguardSquad = SquadFactory.GenerateSquad(targetSquadTemplate.BodyguardSquadTemplate);
                opposingForces.Add(bodyguardSquad);
            }

            return opposingForces;
        }

        private static List<Squad> GenerateScoutPatrol(ForceGenerationRequest request)
        {
            var opposingForces = new List<Squad>();
            var scoutTemplates = request.Faction.SquadTemplates.Values
                                        .Where(st => (st.SquadType & SquadTypes.Scout) != 0).ToList();

            if (!scoutTemplates.Any()) return opposingForces;

            for(int i = request.Tier; i > 0; i--)
            {
                SquadTemplate template = scoutTemplates[RNG.GetIntBelowMax(0, scoutTemplates.Count)];
                opposingForces.Add(SquadFactory.GenerateSquad(template));
            }
            return opposingForces;
        }
    }
}
