using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
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
        private static readonly string[] SquadDesignators =
        [
            "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel",
            "India", "Juliett", "Kilo", "Lima", "Mike", "November", "Oscar", "Papa",
            "Quebec", "Romeo", "Sierra", "Tango", "Uniform", "Victor", "Whiskey", "X-ray",
            "Yankee", "Zulu"
        ];

        public static List<Squad> GenerateForce(ForceGenerationRequest request, IRNG random)
        {
            return GenerateForce(request, random, null);
        }

        public static List<Squad> GenerateForce(
            ForceGenerationRequest request,
            IRNG random,
            IEntityIdAllocator entityIds)
        {
            List<Squad> generatedForce;
            switch (request.Profile)
            {
                case ForceCompositionProfile.SpecialHQTarget:
                    generatedForce = GenerateHqEncounter(request, random, entityIds);
                    break;

                case ForceCompositionProfile.ScoutPatrol:
                    generatedForce = GenerateScoutPatrol(request, random, entityIds);
                    break;

                case ForceCompositionProfile.Garrison:
                case ForceCompositionProfile.AssaultForce:
                case ForceCompositionProfile.AmbushForce:
                default:
                    generatedForce = GenerateGenericForce(request, random, entityIds);
                    break;
            }

            NameGeneratedSquads(generatedForce);
            if (entityIds != null)
            {
                NameTacticalSoldiers(generatedForce);
            }
            return generatedForce;
        }

        private static void NameGeneratedSquads(IReadOnlyList<Squad> generatedSquads)
        {
            foreach (IGrouping<int, Squad> templateGroup in generatedSquads
                .GroupBy(squad => squad.SquadTemplate.Id))
            {
                List<Squad> squads = templateGroup.ToList();
                string templateName = squads[0].SquadTemplate.Name;
                if (squads.Count == 1)
                {
                    squads[0].Name = templateName;
                    continue;
                }

                for (int i = 0; i < squads.Count; i++)
                {
                    squads[i].Name = $"{templateName} {GetSquadDesignator(i)}";
                }
            }
        }

        private static void NameTacticalSoldiers(IEnumerable<Squad> generatedSquads)
        {
            int ordinal = 1;
            foreach (Squad squad in generatedSquads)
            {
                foreach (Soldier soldier in squad.Members.Cast<Soldier>())
                {
                    soldier.Name = $"{soldier.Template.Name} {ordinal++}";
                }
            }
        }

        private static string GetSquadDesignator(int index)
        {
            return index < SquadDesignators.Length
                ? SquadDesignators[index]
                : $"Unit {index + 1}";
        }

        // A faction with no squad-template rows in the rules data gets a NULL SquadTemplates map from
        // GameRulesDataAccess.GetFactions, not an empty one — it only builds the dictionary when the
        // faction has at least one row. The Insurrectionists are exactly that faction today, and every
        // profile below then crashed on the dereference instead of taking its own "no usable template"
        // early-out. That was latent rather than harmless: Region.SelectSpotter simply could not pick a
        // faction with no region-intel while any other faction in the region had some, so a
        // template-less faction was never asked to field an interceptor. Weighting the spotter roll by
        // WatchScore made it reachable, at which point a recon mission against an insurrectionist
        // region threw a NullReferenceException out of the turn processor.
        //
        // Normalizing to an empty sequence is the behaviour the callers already expect: every profile
        // returns an empty force when it finds nothing suitable, and DetectedMissionStep handles that
        // explicitly by logging an uncontested intrusion and letting the mission press on.
        private static IEnumerable<SquadTemplate> AvailableSquadTemplates(
            ForceGenerationRequest request) =>
            request.Faction?.SquadTemplates?.Values ?? Enumerable.Empty<SquadTemplate>();

        private static List<Squad> GenerateGenericForce(
            ForceGenerationRequest request,
            IRNG random,
            IEntityIdAllocator entityIds)
        {
            var generatedSquads = new List<Squad>();
            var usableTemplates = AvailableSquadTemplates(request)
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

            // §9 synapse ratio (OnlyWar_TDD.md §6.6): only factions that field at
            // least one synapse-providing squad template are affected. Every other faction's
            // generation is exactly the loop that existed before this pass.
            List<SquadTemplate> synapseTemplates = usableTemplates
                .Where(st => st.ProvidesSynapse)
                .OrderBy(st => st.BattleValue)
                .ToList();
            bool factionHasSynapse = synapseTemplates.Count > 0;
            float coverageNeedingBvPurchased = 0f;
            int synapseSquadsPurchased = 0;

            long remainingValue = request.TargetBattleValue;
            Squad hqSquad = GenerateGenericHqSquad(
                hqTemplates,
                availableTemplates,
                remainingValue,
                random,
                entityIds);
            if (hqSquad != null)
            {
                generatedSquads.Add(hqSquad);
                long hqValue = SquadBattleValue(hqSquad);
                remainingValue -= hqValue;
                if (factionHasSynapse)
                {
                    TrackSynapseAccounting(
                        hqSquad.SquadTemplate, hqValue,
                        ref coverageNeedingBvPurchased, ref synapseSquadsPurchased);
                }
            }

            HashSet<int> usedTemplateIds = [];

            while (remainingValue > 0)
            {
                // Ratio purchase, interleaved here so budget accounting stays in the one
                // remainingValue variable (§9). Only fires when coverage-needing BV bought
                // so far is owed another synapse squad and one is currently affordable;
                // otherwise generation falls through to the pre-existing selection logic
                // unchanged.
                SquadTemplate chosenTemplate = null;
                if (factionHasSynapse
                    && SynapseSquadsOwed(coverageNeedingBvPurchased, synapseSquadsPurchased) > 0)
                {
                    List<SquadTemplate> affordableSynapse = synapseTemplates
                        .Where(t => t.BattleValue <= remainingValue)
                        .ToList();
                    if (affordableSynapse.Any())
                    {
                        chosenTemplate = affordableSynapse[
                            random.GetIntBelowMax(0, affordableSynapse.Count)];
                    }
                }

                if (chosenTemplate == null)
                {
                    List<SquadTemplate> affordableTemplates = availableTemplates
                        .Where(t => t.BattleValue <= remainingValue)
                        .ToList();
                    if (!affordableTemplates.Any())
                    {
                        Squad partialSquad = GeneratePartialRemainderSquad(
                            availableTemplates,
                            remainingValue,
                            random,
                            entityIds);
                        if (partialSquad != null)
                        {
                            generatedSquads.Add(partialSquad);
                            long partialValue = SquadBattleValue(partialSquad);
                            remainingValue -= partialValue;
                            if (factionHasSynapse)
                            {
                                TrackSynapseAccounting(
                                    partialSquad.SquadTemplate, partialValue,
                                    ref coverageNeedingBvPurchased, ref synapseSquadsPurchased);
                            }
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

                    chosenTemplate = unusedTemplates[
                        random.GetIntBelowMax(0, unusedTemplates.Count)];
                    usedTemplateIds.Add(chosenTemplate.Id);
                }

                Squad generatedSquad = SquadFactory.GenerateSquad(
                    chosenTemplate,
                    random,
                    entityIds);
                generatedSquads.Add(generatedSquad);
                // Charge the budget for the squad that actually mustered, not the template's
                // advertised price. A template with a rolled strength prices at its average, so an
                // understrength mob would otherwise be billed for bodies it never fielded (and an
                // overstrength one would come free). Affordability above still gates on the average,
                // which keeps the loop terminating: every squad costs at least its minimum strength.
                // Floor of 1: a template whose only rolled element came up empty would otherwise
                // cost nothing and spin the loop forever.
                long generatedValue = Math.Max(1, SquadBattleValue(generatedSquad));
                remainingValue -= generatedValue;
                if (factionHasSynapse)
                {
                    TrackSynapseAccounting(
                        chosenTemplate, generatedValue,
                        ref coverageNeedingBvPurchased, ref synapseSquadsPurchased);
                }
            }

            // §9 minimum-force floor: a force that actually bought coverage-needing squads
            // but was too small to afford even the cheapest synapse-providing squad via the
            // ratio still gets one, which can push it above its budgeted BV. Accepted quirk
            // (§9, "Accepted quirk (2026-07-16)") pending a deployment / instinctive-behavior
            // pass, not a bug to fix here. A force with no coverage-needing BV at all (e.g. a
            // pure independent-willed roster) buys no synapse escort it does not need.
            if (factionHasSynapse && synapseSquadsPurchased == 0 && coverageNeedingBvPurchased > 0)
            {
                SquadTemplate cheapestSynapseTemplate = synapseTemplates[0];
                generatedSquads.Add(SquadFactory.GenerateSquad(
                    cheapestSynapseTemplate,
                    random,
                    entityIds));
                synapseSquadsPurchased++;
            }

            // Force-scale trace: one squad is added per unit of BV budget, so a pool-sized budget
            // (e.g. an NPC committing its whole organized force) yields a huge squad/soldier count —
            // the input that makes the tactical resolver explode. Log the result so it is visible.
            GameLog.Debug(() =>
                $"GenerateGenericForce {request.Faction?.Name} {request.Profile}: budget={request.TargetBattleValue}, "
                + $"squads={generatedSquads.Count}, soldiers={generatedSquads.Sum(s => s.Members.Count)}");

            return generatedSquads;
        }

        // §9: a squad "needs coverage" iff it neither provides synapse nor passes the §7 Ego
        // gate. Reusing RearGuardEgoThreshold keeps "independent-willed" defined once.
        private static bool SquadNeedsCoverage(SquadTemplate template) =>
            !template.ProvidesSynapse && template.SquadEgo < MoraleConstants.RearGuardEgoThreshold;

        private static void TrackSynapseAccounting(
            SquadTemplate template,
            long squadValue,
            ref float coverageNeedingBvPurchased,
            ref int synapseSquadsPurchased)
        {
            if (template.ProvidesSynapse)
            {
                synapseSquadsPurchased++;
            }
            else if (SquadNeedsCoverage(template))
            {
                coverageNeedingBvPurchased += squadValue;
            }
        }

        // One synapse-providing squad owed per MoraleConstants.SynapseRatioBV of
        // coverage-needing BV purchased so far, net of synapse squads already bought.
        private static int SynapseSquadsOwed(float coverageNeedingBvPurchased, int synapseSquadsPurchased)
        {
            int owed = (int)(coverageNeedingBvPurchased / MoraleConstants.SynapseRatioBV) - synapseSquadsPurchased;
            return owed > 0 ? owed : 0;
        }

        private static Squad GenerateGenericHqSquad(
            IEnumerable<SquadTemplate> hqTemplates,
            IEnumerable<SquadTemplate> availableNonHqTemplates,
            long budget,
            IRNG random,
            IEntityIdAllocator entityIds)
        {
            List<SquadTemplate> viableHqTemplates = hqTemplates
                .Where(hq => hq.BattleValue <= budget)
                .Where(hq => CanAffordFullSquadCount(availableNonHqTemplates, budget - hq.BattleValue, 3))
                .ToList();

            if (!viableHqTemplates.Any()) return null;

            SquadTemplate template = viableHqTemplates[
                random.GetIntBelowMax(0, viableHqTemplates.Count)];
            return SquadFactory.GenerateSquad(template, random, entityIds);
        }

        private static bool CanAffordFullSquadCount(
            IEnumerable<SquadTemplate> availableTemplates,
            long budget,
            int squadCount)
        {
            int cheapestBattleValue = availableTemplates.Min(t => t.BattleValue);
            return budget >= (long)cheapestBattleValue * squadCount;
        }

        private static Squad GeneratePartialRemainderSquad(
            IEnumerable<SquadTemplate> availableTemplates,
            long remainingValue,
            IRNG random,
            IEntityIdAllocator entityIds)
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
                return SquadFactory.GenerateSquadWithinBudget(
                    template,
                    remainingValue,
                    random,
                    entityIds);
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

        private static List<Squad> GenerateHqEncounter(
            ForceGenerationRequest request,
            IRNG random,
            IEntityIdAllocator entityIds)
        {
            var opposingForces = new List<Squad>();
            var sortedHqSquads = AvailableSquadTemplates(request)
                                .Where(st => (st.SquadType & SquadTypes.HQ) == SquadTypes.HQ)
                                .OrderBy(st => st.BattleValue)
                                .ToList();

            if (!sortedHqSquads.Any()) return opposingForces;

            int index = Math.Clamp(request.Tier - 1, 0, sortedHqSquads.Count - 1);
            SquadTemplate targetSquadTemplate = sortedHqSquads[index];
            Squad hqSquad = SquadFactory.GenerateSquad(targetSquadTemplate, random, entityIds);
            opposingForces.Add(hqSquad);

            // Use the BattleValue to determine if a bodyguard should be added
            if (request.TargetBattleValue <= 0 && targetSquadTemplate.BodyguardSquadTemplate != null)
            {
                Squad bodyguardSquad = SquadFactory.GenerateSquad(
                    targetSquadTemplate.BodyguardSquadTemplate,
                    random,
                    entityIds);
                opposingForces.Add(bodyguardSquad);
            }

            return opposingForces;
        }

        private static List<Squad> GenerateScoutPatrol(
            ForceGenerationRequest request,
            IRNG random,
            IEntityIdAllocator entityIds)
        {
            var opposingForces = new List<Squad>();
            var scoutTemplates = AvailableSquadTemplates(request)
                                        .Where(st => (st.SquadType & SquadTypes.Scout) != 0).ToList();

            // NOTE: a faction with no Scout-typed squad templates fields no ScoutPatrol force at all.
            // Verified against the rules DB 2026-07-29: the only such faction is the Orks, which have a
            // single template (Ork Warboss, HQ) because the faction is not implemented yet. The
            // Genestealer Cults are no longer affected - Acolyte Hybrids, Brood Brother Squad and
            // Neophyte Hybrid Squad are all Scout-flagged - and the Imperial PDF Infantry Squad was
            // flagged Scout by migrate-scout-skills.
            //
            // The consequence is narrower than it used to be. This profile no longer feeds interception:
            // DetectedMissionStep intercepts with the squads a region actually has out looking and
            // conjures nothing. What an empty result means now is that a faction cannot field a standing
            // patrol screen (FactionStrategyController.PlanPatrolMissionsOnPlanet), so it neither sweeps
            // its own ground nor intercepts anyone. The fix remains a data pass giving such factions
            // small scout/patrol squad templates (and Tactics skill), not a line-squad fallback here:
            // scrambling *line* squads produced multi-squad (20-50 soldier) interceptors that overwhelmed
            // the scout, exposed a placement crash in AmbushPlacer for large forces, and slowed the sim.
            if (!scoutTemplates.Any()) return opposingForces;

            for(int i = request.Tier; i > 0; i--)
            {
                SquadTemplate template = scoutTemplates[
                    random.GetIntBelowMax(0, scoutTemplates.Count)];
                opposingForces.Add(SquadFactory.GenerateSquad(template, random, entityIds));
            }
            return opposingForces;
        }
    }
}
