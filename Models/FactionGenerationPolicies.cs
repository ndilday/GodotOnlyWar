using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models
{
    /// <summary>
    /// Stable roles that identify faction responsibilities outside the faction's display identity.
    /// These are code-owned contracts; the rules database chooses which faction fills each role.
    /// </summary>
    public static class FactionRoleKeys
    {
        public const string Infiltrator = "sector.infiltrator";
        public const string Invader = "sector.invader";
        public const string Insurrectionists = "sector.insurrectionists";

        public static bool TryParse(string value, out string roleKey)
        {
            string candidate = value?.Trim();
            if (string.Equals(candidate, Infiltrator, StringComparison.OrdinalIgnoreCase))
            {
                roleKey = Infiltrator;
                return true;
            }
            if (string.Equals(candidate, Invader, StringComparison.OrdinalIgnoreCase))
            {
                roleKey = Invader;
                return true;
            }
            if (string.Equals(candidate, Insurrectionists, StringComparison.OrdinalIgnoreCase))
            {
                roleKey = Insurrectionists;
                return true;
            }
            roleKey = candidate;
            return false;
        }
    }

    /// <summary>
    /// Data-owned assignment of a stable faction role to a rules-database faction row.
    /// </summary>
    public sealed record FactionRoleAssignment(string RoleKey, int FactionId);

    public static class ScenarioKeys
    {
        public const string PromisedWorld = "promised_world";
    }

    public static class ScenarioFactionSlotKeys
    {
        public const string Infiltrator = "infiltrator";
        public const string Invader = "invader";

        public static bool TryParse(string value, out string slotKey)
        {
            string candidate = value?.Trim();
            if (string.Equals(candidate, Infiltrator, StringComparison.OrdinalIgnoreCase))
            {
                slotKey = Infiltrator;
                return true;
            }
            if (string.Equals(candidate, Invader, StringComparison.OrdinalIgnoreCase))
            {
                slotKey = Invader;
                return true;
            }
            slotKey = candidate;
            return false;
        }
    }

    /// <summary>
    /// A candidate faction for a scenario slot. Multiple candidates are selected by weight at
    /// generation time; a single candidate keeps the generation stream unchanged.
    /// </summary>
    public sealed record ScenarioFactionOption(
        string ScenarioKey,
        string SlotKey,
        int FactionId,
        double SelectionWeight,
        bool IsRequired);

    /// <summary>
    /// Balance and participant inputs for one implemented opening scenario. The scenario algorithm
    /// remains code-owned; these values and its faction candidates are mod-owned data.
    /// </summary>
    public sealed class ScenarioProfile
    {
        private readonly IReadOnlyList<ScenarioFactionOption> _factionOptions;

        public string Key { get; }
        public long MaxPromisedWorldPopulation { get; }
        public int MinInvaderRegions { get; }
        public int MaxInvaderRegions { get; }
        public float InvaderGarrisonStrengthMultiple { get; }
        public float ImperialRemnantFraction { get; }
        public int PreLandingTurns { get; }
        public float InitialInfiltratorPopulationShareMin { get; }
        public float InitialInfiltratorPopulationShareMax { get; }
        public float InitialInfiltratorGarrisonPerPopulation { get; }
        public float PromisedWorldInfiltratorStrengthFraction { get; }
        public float PromisedWorldInfiltratorStartingIntel { get; }
        public double PostLandingTurnsMean { get; }
        public float SectorLordOpinionReward { get; }
        public float SectorLordOpinionPenalty { get; }

        public ScenarioProfile(
            string key,
            long maxPromisedWorldPopulation,
            int minInvaderRegions,
            int maxInvaderRegions,
            float invaderGarrisonStrengthMultiple,
            float imperialRemnantFraction,
            int preLandingTurns,
            float initialInfiltratorPopulationShareMin,
            float initialInfiltratorPopulationShareMax,
            float initialInfiltratorGarrisonPerPopulation,
            float promisedWorldInfiltratorStrengthFraction,
            float promisedWorldInfiltratorStartingIntel,
            double postLandingTurnsMean,
            float sectorLordOpinionReward,
            float sectorLordOpinionPenalty,
            IEnumerable<ScenarioFactionOption> factionOptions)
        {
            Key = key;
            MaxPromisedWorldPopulation = maxPromisedWorldPopulation;
            MinInvaderRegions = minInvaderRegions;
            MaxInvaderRegions = maxInvaderRegions;
            InvaderGarrisonStrengthMultiple = invaderGarrisonStrengthMultiple;
            ImperialRemnantFraction = imperialRemnantFraction;
            PreLandingTurns = preLandingTurns;
            InitialInfiltratorPopulationShareMin = initialInfiltratorPopulationShareMin;
            InitialInfiltratorPopulationShareMax = initialInfiltratorPopulationShareMax;
            InitialInfiltratorGarrisonPerPopulation = initialInfiltratorGarrisonPerPopulation;
            PromisedWorldInfiltratorStrengthFraction = promisedWorldInfiltratorStrengthFraction;
            PromisedWorldInfiltratorStartingIntel = promisedWorldInfiltratorStartingIntel;
            PostLandingTurnsMean = postLandingTurnsMean;
            SectorLordOpinionReward = sectorLordOpinionReward;
            SectorLordOpinionPenalty = sectorLordOpinionPenalty;
            _factionOptions = (factionOptions ?? []).ToList();
        }

        public IReadOnlyList<ScenarioFactionOption> FactionOptions => _factionOptions;

        public IReadOnlyList<ScenarioFactionOption> GetFactionOptions(string slotKey) =>
            _factionOptions
                .Where(option => string.Equals(option.SlotKey, slotKey, StringComparison.OrdinalIgnoreCase))
                .OrderBy(option => option.FactionId)
                .ToList();
    }

    public sealed class ScenarioProfileCatalog
    {
        private readonly IReadOnlyDictionary<string, ScenarioProfile> _profiles;

        public ScenarioProfileCatalog(IEnumerable<ScenarioProfile> profiles)
        {
            Dictionary<string, ScenarioProfile> map = new(StringComparer.OrdinalIgnoreCase);
            foreach (ScenarioProfile profile in profiles ?? [])
            {
                if (profile == null || string.IsNullOrWhiteSpace(profile.Key))
                {
                    throw new InvalidOperationException("A scenario profile has no key.");
                }
                if (!map.TryAdd(profile.Key, profile))
                {
                    throw new InvalidOperationException(
                        $"Scenario profile '{profile.Key}' is defined more than once.");
                }
            }
            _profiles = map;
        }

        public IReadOnlyDictionary<string, ScenarioProfile> Profiles => _profiles;

        public ScenarioProfile GetRequired(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || !_profiles.TryGetValue(key, out ScenarioProfile profile))
            {
                throw new InvalidOperationException(
                    $"Required scenario profile '{key}' was not found in the rules database.");
            }
            return profile;
        }
    }

    public enum FactionPresenceMode
    {
        Hidden = 0,
        Public = 1
    }

    /// <summary>
    /// Declarative initial presence policy for a faction. PlanetTemplateId is null for a default
    /// rule and otherwise narrows the rule to one planet archetype.
    /// </summary>
    public sealed record FactionPlanetPresenceRule(
        string ProfileKey,
        int FactionId,
        int? PlanetTemplateId,
        FactionPresenceMode PresenceMode,
        double SpawnChance,
        double PopulationShareMin,
        double PopulationShareMax,
        double GarrisonPerPopulation);

    public static class SectorGenerationProfileKeys
    {
        public const string Standard = "standard";
    }

    public sealed class FactionPlanetPresenceCatalog
    {
        private readonly IReadOnlyList<FactionPlanetPresenceRule> _rules;

        public FactionPlanetPresenceCatalog(IEnumerable<FactionPlanetPresenceRule> rules)
        {
            _rules = (rules ?? []).ToList();
        }

        public IReadOnlyList<FactionPlanetPresenceRule> Rules => _rules;

        public IReadOnlyList<FactionPlanetPresenceRule> GetApplicableRules(
            string profileKey,
            int planetTemplateId) =>
            _rules
                .Where(rule => string.Equals(rule.ProfileKey, profileKey, StringComparison.OrdinalIgnoreCase)
                    && (!rule.PlanetTemplateId.HasValue || rule.PlanetTemplateId.Value == planetTemplateId))
                // Keep only the most-specific rule for each faction. This prevents a failed
                // template-specific roll from falling through to the profile-wide default.
                .GroupBy(rule => rule.FactionId)
                .Select(group => group
                    .OrderByDescending(rule => rule.PlanetTemplateId.HasValue)
                    .First())
                // A template-specific rule is an override of the profile-wide default for the
                // same faction, so it must be applied first.
                .OrderBy(rule => rule.PlanetTemplateId.HasValue ? 0 : 1)
                .ThenBy(rule => rule.FactionId)
                .ToList();
    }
}
