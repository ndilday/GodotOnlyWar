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
        public long TargetBattleValue;
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
            var usableTemplates = request.Faction.SquadTemplates.Values
                                             .Where(st => st.BattleValue > 0)
                                             .OrderBy(st => st.Id)
                                             .ToList();
            var availableTemplates = usableTemplates
                                            .Where(st => (st.SquadType & SquadTypes.HQ) == 0)
                                            .ToList();
            var hqTemplates = usableTemplates
                                            .Where(st => (st.SquadType & SquadTypes.HQ) == SquadTypes.HQ)
                                            .ToList();

            if (!availableTemplates.Any()) return new List<Squad>();

            long remainingValue = request.TargetBattleValue;
            Squad hqSquad = GenerateGenericHqSquad(hqTemplates, availableTemplates, remainingValue);
            if (hqSquad != null)
            {
                generatedSquads.Add(hqSquad);
                remainingValue -= SquadBattleValue(hqSquad);
            }

            HashSet<int> usedTemplateIds = [];

            while (remainingValue > 0)
            {
                List<SquadTemplate> affordableTemplates = availableTemplates
                    .Where(t => t.BattleValue <= remainingValue)
                    .ToList();
                if (!affordableTemplates.Any())
                {
                    Squad partialSquad = GeneratePartialRemainderSquad(availableTemplates, remainingValue);
                    if (partialSquad != null)
                    {
                        generatedSquads.Add(partialSquad);
                        remainingValue -= SquadBattleValue(partialSquad);
                    }
                    break;
                }

                List<SquadTemplate> viableTemplates = affordableTemplates
                    .Where(t => LeavesUsableRemainder(availableTemplates, remainingValue - t.BattleValue))
                    .ToList();
                if (viableTemplates.Any())
                {
                    affordableTemplates = viableTemplates;
                }

                List<SquadTemplate> unusedTemplates = affordableTemplates
                    .Where(t => !usedTemplateIds.Contains(t.Id))
                    .ToList();
                if (!unusedTemplates.Any())
                {
                    usedTemplateIds.Clear();
                    unusedTemplates = affordableTemplates;
                }

                SquadTemplate affordableTemplate = unusedTemplates[RNG.GetIntBelowMax(0, unusedTemplates.Count)];
                usedTemplateIds.Add(affordableTemplate.Id);
                generatedSquads.Add(SquadFactory.GenerateSquad(affordableTemplate));
                remainingValue -= affordableTemplate.BattleValue;
            }

            // Force-scale trace: one squad is added per unit of BV budget, so a pool-sized budget
            // (e.g. an NPC committing its whole organized force) yields a huge squad/soldier count —
            // the input that makes the tactical resolver explode. Log the result so it is visible.
            GameLog.Debug(() =>
                $"GenerateGenericForce {request.Faction?.Name} {request.Profile}: budget={request.TargetBattleValue}, "
                + $"squads={generatedSquads.Count}, soldiers={generatedSquads.Sum(s => s.Members.Count)}");

            return generatedSquads;
        }

        private static Squad GenerateGenericHqSquad(
            IEnumerable<SquadTemplate> hqTemplates,
            IEnumerable<SquadTemplate> availableNonHqTemplates,
            long budget)
        {
            List<SquadTemplate> viableHqTemplates = hqTemplates
                .Where(hq => hq.BattleValue <= budget)
                .Where(hq => CanAffordFullSquadCount(availableNonHqTemplates, budget - hq.BattleValue, 3))
                .ToList();

            if (!viableHqTemplates.Any()) return null;

            SquadTemplate template = viableHqTemplates[RNG.GetIntBelowMax(0, viableHqTemplates.Count)];
            return SquadFactory.GenerateSquad(template);
        }

        private static bool CanAffordFullSquadCount(
            IEnumerable<SquadTemplate> availableTemplates,
            long budget,
            int squadCount)
        {
            int cheapestBattleValue = availableTemplates.Min(t => t.BattleValue);
            return budget >= (long)cheapestBattleValue * squadCount;
        }

        private static Squad GeneratePartialRemainderSquad(IEnumerable<SquadTemplate> availableTemplates, long remainingValue)
        {
            SquadTemplate template = availableTemplates
                .Select(t => new
                {
                    Template = t,
                    BattleValue = SquadFactory.CalculateSquadBattleValueWithinBudget(t, remainingValue)
                })
                .Where(t => t.BattleValue > 0)
                .OrderByDescending(t => t.BattleValue)
                .ThenBy(t => t.Template.Id)
                .Select(t => t.Template)
                .FirstOrDefault();

            if (template != null)
            {
                return SquadFactory.GenerateSquadWithinBudget(template, remainingValue);
            }

            return null;
        }

        private static bool LeavesUsableRemainder(IEnumerable<SquadTemplate> availableTemplates, long remainingValue)
        {
            if (remainingValue == 0) return true;
            return availableTemplates.Any(t => t.BattleValue <= remainingValue)
                || availableTemplates.Any(t => SquadFactory.CalculateSquadBattleValueWithinBudget(t, remainingValue) > 0);
        }

        private static long SquadBattleValue(Squad squad) =>
            squad.Members.Sum(member => (long)member.Template.BattleValue);

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

            // NOTE: a faction with no Scout-typed squad templates (currently the Genestealer Cults
            // and Orks — the Imperial PDF Infantry Squad was flagged Scout by migrate-scout-skills)
            // fields no interception here, so recon against them runs uncontested. Making the
            // garrison scramble its *line* squads
            // instead produced multi-squad (20-50 soldier) interceptors that overwhelmed the scout,
            // exposed a placement crash in AmbushPlacer for large forces, and slowed the sim sharply.
            // The right fix is a data pass giving those factions small scout/patrol squad templates
            // (and Tactics skill), not a line-squad fallback here.
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
