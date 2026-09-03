using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Models.FactionBehaviors;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Database.GameRules
{
    /// <summary>
    /// Validates the hydrated rules profile before it becomes available to campaign generation.
    /// This is intentionally separate from individual semantic registries: those registries
    /// validate a particular code-owned contract, while this class checks the general integrity
    /// and availability guarantees shared by all rules consumers.
    /// </summary>
    internal static class RulesDatabaseValidator
    {
        public static void Validate(GameRulesBlob rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));

            List<string> errors = [];

            RequireNotEmpty(rules.Factions, "Faction", errors);
            RequireNotEmpty(rules.BaseSkills, "BaseSkill", errors);
            RequireNotEmpty(rules.SkillTemplates, "SkillTemplate", errors);
            RequireNotEmpty(rules.BodyTemplates, "HitLocationTemplate grouped by body", errors);
            RequireNotEmpty(rules.PlanetTemplates, "PlanetTemplate", errors);
            RequireNotEmpty(
                rules.PlanetTemplateEligibilityAssignments,
                "PlanetTemplateEligibility",
                errors);
            RequireNotEmpty(rules.MeleeWeaponTemplates, "MeleeWeaponTemplate", errors);
            RequireNotEmpty(rules.RangedWeaponTemplates, "RangedWeaponTemplate", errors);
            RequireNotEmpty(rules.WeaponSets, "WeaponSet", errors);
            RequireNotEmpty(rules.TrainingProfiles, "TrainingProfile", errors);
            RequireNotEmpty(rules.ScoutTrainingOptions?.Options, "ScoutTrainingOption", errors);
            RequireNotEmpty(rules.RatingDefinitions, "RatingDefinition", errors);
            RequireNotEmpty(rules.RatingAwardTiers, "RatingAwardTier", errors);
            RequireNotEmpty(rules.FactionRoleAssignments, "FactionRoleAssignment", errors);
            RequireNotEmpty(rules.ScenarioProfiles, "ScenarioProfile", errors);
            RequireNotEmpty(rules.ScenarioFactionOptions, "ScenarioFactionOption", errors);
            RequireNotEmpty(rules.FactionPlanetPresenceRules, "FactionPlanetPresenceRule", errors);
            RequireNotEmpty(rules.ChapterGenerationProfiles, "ChapterGenerationProfile", errors);
            RequireNotEmpty(rules.SectorGenerationProfiles, "SectorGenerationProfile", errors);
            RequireNotEmpty(
                rules.FactionBehaviorRulesProfiles,
                "FactionBehaviorRulesProfile (or the legacy faction behavior table)",
                errors);

            ValidateSectorGenerationProfiles(rules.SectorGenerationProfiles, errors);
            ValidatePlanetTemplates(rules.PlanetTemplates, errors);
            ValidatePlanetTemplateEligibility(
                rules.PlanetTemplates,
                rules.PlanetTemplateEligibilityAssignments,
                errors);
            ValidateSkillTemplates(rules.SkillTemplates, errors);
            ValidateTrainingProfiles(rules.TrainingProfiles, errors);
            ValidateScoutTrainingOptions(rules.ScoutTrainingOptions, errors);
            ValidateFactionContent(rules, errors);
            ValidateEquipmentCatalog(rules.EquipmentCatalog, errors);
            ValidateFactionBehaviorProfiles(rules.FactionBehaviorRulesProfiles, errors);

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Rules database validation failed:\n - " + string.Join("\n - ", errors));
            }
        }

        private static void ValidateFactionBehaviorProfiles(
            IReadOnlyList<FactionBehaviorRulesProfile> profiles,
            ICollection<string> errors)
        {
            if (profiles == null) return;
            HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
            foreach (FactionBehaviorRulesProfile profile in profiles)
            {
                if (profile == null)
                {
                    errors.Add("FactionBehaviorRulesProfile contains a null row.");
                    continue;
                }
                if (!keys.Add(profile.Key ?? string.Empty))
                {
                    errors.Add($"FactionBehaviorRulesProfile '{profile.Key}' is defined more than once.");
                    continue;
                }
                try { profile.Validate(); }
                catch (InvalidOperationException exception) { errors.Add(exception.Message); }
            }
        }

        private static void ValidateSectorGenerationProfiles(
            IReadOnlyList<SectorGenerationProfile> profiles,
            ICollection<string> errors)
        {
            if (profiles == null || profiles.Count == 0) return;

            HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
            int defaultCount = 0;
            foreach (SectorGenerationProfile profile in profiles)
            {
                if (profile == null)
                {
                    errors.Add("SectorGenerationProfile contains a null row.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(profile.Key))
                {
                    errors.Add("SectorGenerationProfile requires a profile key.");
                }
                else if (!keys.Add(profile.Key.Trim()))
                {
                    errors.Add(
                        $"SectorGenerationProfile '{profile.Key}' is defined more than once.");
                }

                if (profile.IsDefault) defaultCount++;
                if (profile.SectorWidth <= 0 || profile.SectorHeight <= 0)
                {
                    errors.Add(
                        $"SectorGenerationProfile '{profile.Key}' must have positive sector "
                        + "dimensions.");
                }
                if (profile.MaxSubsectorDiameter <= 0)
                {
                    errors.Add(
                        $"SectorGenerationProfile '{profile.Key}' must have a positive "
                        + "maximum subsector diameter.");
                }
                if (double.IsNaN(profile.PlanetSpawnProbability)
                    || double.IsInfinity(profile.PlanetSpawnProbability)
                    || profile.PlanetSpawnProbability < 0
                    || profile.PlanetSpawnProbability > 1)
                {
                    errors.Add(
                        $"SectorGenerationProfile '{profile.Key}' planet spawn probability "
                        + "must be finite and within [0, 1].");
                }
            }

            if (defaultCount != 1)
            {
                errors.Add(
                    "Rules database must define exactly one default SectorGenerationProfile; "
                    + $"found {defaultCount}.");
            }
        }

        private static void ValidatePlanetTemplates(
            IReadOnlyDictionary<int, PlanetTemplate> templates,
            ICollection<string> errors)
        {
            if (templates == null || templates.Count == 0) return;

            List<PlanetTemplate> invalid = templates.Values
                .Where(template => template == null || template.Probability < 0)
                .ToList();
            if (invalid.Count > 0)
            {
                errors.Add(
                    "PlanetTemplate probabilities must be non-negative; invalid template ids: "
                    + string.Join(", ", invalid.Select(template => template?.Id.ToString() ?? "<null>"))
                    + ".");
            }

            long totalProbability = templates.Values
                .Where(template => template != null)
                .Sum(template => (long)template.Probability);
            if (totalProbability <= 0)
            {
                errors.Add("PlanetTemplate probabilities must have a positive total.");
            }
        }

        private static void ValidatePlanetTemplateEligibility(
            IReadOnlyDictionary<int, PlanetTemplate> templates,
            IReadOnlyList<PlanetTemplateEligibilityAssignment> assignments,
            ICollection<string> errors)
        {
            if (templates == null || templates.Count == 0
                || assignments == null || assignments.Count == 0)
            {
                return;
            }

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (PlanetTemplateEligibilityAssignment assignment in assignments)
            {
                if (assignment == null || string.IsNullOrWhiteSpace(assignment.ContextKey))
                {
                    errors.Add("PlanetTemplateEligibility assignments require a context key.");
                    continue;
                }

                string contextKey = assignment.ContextKey.Trim();
                if (!seen.Add($"{contextKey}\u001f{assignment.PlanetTemplateId}"))
                {
                    errors.Add(
                        $"PlanetTemplateEligibility context '{contextKey}' assigns "
                        + $"template id {assignment.PlanetTemplateId} more than once.");
                }
                if (!templates.ContainsKey(assignment.PlanetTemplateId))
                {
                    errors.Add(
                        $"PlanetTemplateEligibility context '{contextKey}' references "
                        + $"missing PlanetTemplate id {assignment.PlanetTemplateId}.");
                }
            }

            foreach (string requiredContext in new[]
            {
                PlanetTemplateEligibilityKeys.PromisedWorld,
                PlanetTemplateEligibilityKeys.GhostPopulationSource
            })
            {
                List<PlanetTemplateEligibilityAssignment> contextAssignments = assignments
                    .Where(assignment => assignment != null
                        && string.Equals(
                            assignment.ContextKey,
                            requiredContext,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (contextAssignments.Count == 0)
                {
                    errors.Add(
                        $"PlanetTemplateEligibility must define at least one template for "
                        + $"context '{requiredContext}'.");
                    continue;
                }

                long totalProbability = contextAssignments
                    .Where(assignment => templates.TryGetValue(
                        assignment.PlanetTemplateId,
                        out PlanetTemplate template) && template != null)
                    .Sum(assignment => (long)templates[assignment.PlanetTemplateId].Probability);
                if (totalProbability <= 0)
                {
                    errors.Add(
                        $"PlanetTemplateEligibility context '{requiredContext}' must have "
                        + "a positive total PlanetTemplate probability.");
                }
            }
        }

        private static void ValidateSkillTemplates(
            IReadOnlyList<SkillTemplate> templates,
            ICollection<string> errors)
        {
            if (templates == null) return;

            foreach (SkillTemplate template in templates)
            {
                if (template == null)
                {
                    errors.Add("SkillTemplate contains a null row.");
                    continue;
                }
                if (template.BaseSkill == null)
                {
                    errors.Add("SkillTemplate has a row with no base skill reference.");
                }
            }
        }

        private static void ValidateTrainingProfiles(
            IReadOnlyDictionary<int, TrainingProfile> profiles,
            ICollection<string> errors)
        {
            if (profiles == null) return;

            foreach ((int id, TrainingProfile profile) in profiles)
            {
                if (profile == null)
                {
                    errors.Add($"TrainingProfile {id} is null.");
                    continue;
                }
                if (profile.Entries == null || profile.Entries.Count == 0)
                {
                    errors.Add(
                        $"TrainingProfile {id} ('{profile.Name}') has no training entries.");
                }
                else if (profile.Entries.Any(entry => entry == null
                    || !Enum.IsDefined(entry.TargetType)
                    || (entry.TargetType == TrainingTargetType.Skill && entry.Skill == null)
                    || (entry.TargetType == TrainingTargetType.Attribute && !entry.Attribute.HasValue)
                    || float.IsNaN(entry.Weight)
                    || float.IsInfinity(entry.Weight)
                    || entry.Weight <= 0))
                {
                    errors.Add(
                        $"TrainingProfile {id} ('{profile.Name}') has an invalid training entry.");
                }
            }
        }

        private static void ValidateScoutTrainingOptions(
            ScoutTrainingOptionCatalog catalog,
            ICollection<string> errors)
        {
            if (catalog == null || catalog.Options == null) return;

            foreach (ScoutTrainingOption option in catalog.Options)
            {
                if (option == null)
                {
                    errors.Add("ScoutTrainingOption contains a null row.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(option.Key))
                {
                    errors.Add("ScoutTrainingOption has an empty stable key.");
                }
                if (string.IsNullOrWhiteSpace(option.DisplayName))
                {
                    errors.Add($"ScoutTrainingOption '{option.Key}' has an empty display name.");
                }
                if (option.Profile == null)
                {
                    errors.Add($"ScoutTrainingOption '{option.Key}' has no training profile.");
                }
            }

            if (!catalog.TryGet(ScoutTrainingOptionKeys.Balanced, out _))
            {
                errors.Add(
                    $"ScoutTrainingOption catalog is missing the required default option "
                    + $"'{ScoutTrainingOptionKeys.Balanced}'.");
            }
        }

        private static void ValidateFactionContent(GameRulesBlob rules, ICollection<string> errors)
        {
            if (rules.Factions == null) return;

            HashSet<int> referencedFactionIds = rules.Factions
                .Where(faction => faction != null)
                .Where(faction => faction.IsPlayerFaction || faction.IsDefaultFaction)
                .Select(faction => faction.Id)
                .ToHashSet();
            referencedFactionIds.UnionWith((rules.FactionRoleAssignments ?? [])
                .Where(assignment => assignment != null)
                .Select(assignment => assignment.FactionId));
            referencedFactionIds.UnionWith((rules.ScenarioFactionOptions ?? [])
                .Where(option => option != null)
                .Select(option => option.FactionId));
            referencedFactionIds.UnionWith((rules.FactionPlanetPresenceRules ?? [])
                .Where(rule => rule != null)
                .Select(rule => rule.FactionId));

            foreach (IGrouping<int, Faction> duplicateGroup in rules.Factions
                         .Where(faction => faction != null)
                         .GroupBy(faction => faction.Id)
                         .Where(group => group.Count() > 1))
            {
                errors.Add(
                    "Rules database contains duplicate faction id "
                    + $"{duplicateGroup.Key} ({duplicateGroup.Count()} rows).");
            }

            Dictionary<int, Faction> factionsById = rules.Factions
                .Where(faction => faction != null)
                .GroupBy(faction => faction.Id)
                .ToDictionary(group => group.Key, group => group.First());

            foreach (int factionId in referencedFactionIds)
            {
                if (!factionsById.TryGetValue(factionId, out Faction faction))
                {
                    errors.Add($"Rules data references missing faction id {factionId}.");
                    continue;
                }

                RequireNotEmpty(faction.Species, $"Faction '{faction.Name}' Species", errors);
                RequireNotEmpty(faction.SoldierTemplates,
                    $"Faction '{faction.Name}' SoldierTemplate", errors);
                RequireNotEmpty(faction.SquadTemplates,
                    $"Faction '{faction.Name}' SquadTemplate", errors);
                ValidateFactionTemplates(faction, errors);
            }

            Faction playerFaction = rules.Factions.FirstOrDefault(faction => faction?.IsPlayerFaction == true);
            if (playerFaction != null)
            {
                ValidatePlayerFleetPrerequisites(playerFaction, errors);
            }
        }

        private static void ValidatePlayerFleetPrerequisites(
            Faction playerFaction,
            ICollection<string> errors)
        {
            RequireNotEmpty(playerFaction.BoatTemplates,
                $"Player faction '{playerFaction.Name}' BoatTemplate", errors);
            RequireNotEmpty(playerFaction.ShipTemplates,
                $"Player faction '{playerFaction.Name}' ShipTemplate", errors);
            RequireNotEmpty(playerFaction.FleetTemplates,
                $"Player faction '{playerFaction.Name}' FleetTemplate", errors);

            foreach (BoatTemplate boat in playerFaction.BoatTemplates?.Values ?? [])
            {
                if (boat == null)
                {
                    errors.Add(
                        $"Player faction '{playerFaction.Name}' contains a null BoatTemplate row.");
                }
            }

            foreach (ShipTemplate ship in playerFaction.ShipTemplates?.Values ?? [])
            {
                if (ship == null)
                {
                    errors.Add(
                        $"Player faction '{playerFaction.Name}' contains a null ShipTemplate row.");
                }
            }

            foreach (FleetTemplate fleet in playerFaction.FleetTemplates?.Values ?? [])
            {
                if (fleet == null)
                {
                    errors.Add(
                        $"Player faction '{playerFaction.Name}' contains a null FleetTemplate row.");
                    continue;
                }
                if (fleet.Ships == null || fleet.Ships.Count == 0)
                {
                    errors.Add(
                        $"Player faction '{playerFaction.Name}' fleet template '{fleet.Name}' "
                        + "has no ship templates.");
                    continue;
                }
                if (fleet.Ships.Any(ship => ship == null))
                {
                    errors.Add(
                        $"Player faction '{playerFaction.Name}' fleet template '{fleet.Name}' "
                        + "contains a null ShipTemplate row.");
                }
            }
        }

        private static void ValidateFactionTemplates(Faction faction, ICollection<string> errors)
        {
            foreach (Species species in faction.Species?.Values ?? [])
            {
                if (species == null)
                {
                    errors.Add($"Faction '{faction.Name}' contains a null Species row.");
                    continue;
                }
                if (species.BodyTemplate == null || species.BodyTemplate.HitLocations == null
                    || species.BodyTemplate.HitLocations.Length == 0)
                {
                    errors.Add(
                        $"Faction '{faction.Name}' Species '{species.Name}' has no hit-location body template.");
                }
                if (species.DefaultUnarmedWeapon == null)
                {
                    errors.Add(
                        $"Faction '{faction.Name}' Species '{species.Name}' has no default unarmed weapon.");
                }
            }

            foreach (SoldierTemplate template in faction.SoldierTemplates?.Values ?? [])
            {
                if (template == null)
                {
                    errors.Add($"Faction '{faction.Name}' contains a null SoldierTemplate row.");
                    continue;
                }
                if (template.Species == null)
                {
                    errors.Add(
                        $"Faction '{faction.Name}' SoldierTemplate '{template.Name}' has no species reference.");
                }
                if (template.MosTraining != null
                    && template.MosTraining.Any(training => training.Item1 == null))
                {
                    errors.Add(
                        $"Faction '{faction.Name}' SoldierTemplate '{template.Name}' has a missing MOS skill reference.");
                }
            }

            foreach (SquadTemplate template in faction.SquadTemplates?.Values ?? [])
            {
                if (template == null)
                {
                    errors.Add($"Faction '{faction.Name}' contains a null SquadTemplate row.");
                    continue;
                }
                if (template.Armor == null)
                {
                    errors.Add(
                        $"Faction '{faction.Name}' SquadTemplate '{template.Name}' has no armor reference.");
                }
                if (template.DefaultWeapons == null)
                {
                    errors.Add(
                        $"Faction '{faction.Name}' SquadTemplate '{template.Name}' has no default weapon-set reference.");
                }
                if (template.Elements == null || template.Elements.Count == 0)
                {
                    errors.Add(
                        $"Faction '{faction.Name}' SquadTemplate '{template.Name}' has no element rows.");
                }
                else if (template.Elements.Any(element => element == null || element.SoldierTemplate == null))
                {
                    errors.Add(
                        $"Faction '{faction.Name}' SquadTemplate '{template.Name}' has an element with no soldier-template reference.");
                }
            }
        }

        private static void ValidateEquipmentCatalog(
            EquipmentRulesCatalog catalog,
            ICollection<string> errors)
        {
            if (catalog == null)
            {
                errors.Add("Equipment rules catalog was not loaded.");
                return;
            }
            RequireNotEmpty(catalog.EquipmentTemplates, "EquipmentTemplate", errors);
            RequireNotEmpty(catalog.EquipmentKits, "EquipmentKitTemplate", errors);
        }

        private static void RequireNotEmpty<T>(
            IReadOnlyCollection<T> collection,
            string name,
            ICollection<string> errors)
        {
            if (collection == null || collection.Count == 0)
            {
                errors.Add($"Required rules collection '{name}' is empty.");
            }
        }

        private static void RequireNotEmpty<TKey, TValue>(
            IReadOnlyDictionary<TKey, TValue> collection,
            string name,
            ICollection<string> errors)
        {
            if (collection == null || collection.Count == 0)
            {
                errors.Add($"Required rules collection '{name}' is empty.");
            }
        }
    }
}
