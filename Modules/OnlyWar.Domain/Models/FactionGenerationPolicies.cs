using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Domain
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

    /// <summary>
    /// Optional profile-local override for the opening infiltrator. The presence of this record
    /// is the trigger for scenario-owned infiltrator setup; a profile without one keeps the
    /// normal infiltrator generation path.
    /// </summary>
    public sealed record ScenarioInfiltratorOverride(
        string ProfileKey,
        int FactionId,
        int PreLandingTurns,
        float InitialPopulationShareMin,
        float InitialPopulationShareMax,
        float InitialGarrisonPerPopulation,
        float StrengthFraction,
        float StartingIntel);

    /// <summary>
    /// The player's opening-scenario choice. A null faction id with IsRandom false means the
    /// scenario's stable default profile; IsRandom true requests weighted selection from the
    /// scenario's eligible profiles. The resolved faction id is what belongs in persistent
    /// campaign state, not this setup-time choice.
    /// </summary>
    public sealed record ScenarioFactionSelection
    {
        public int? FactionId { get; }
        public bool IsRandom { get; }

        private ScenarioFactionSelection(int? factionId, bool isRandom)
        {
            FactionId = factionId;
            IsRandom = isRandom;
        }

        public static ScenarioFactionSelection Default { get; } = new(null, false);
        public static ScenarioFactionSelection Random { get; } = new(null, true);

        public static ScenarioFactionSelection ForFaction(int factionId) =>
            new(factionId, false);
    }

    /// <summary>
    /// Balance and participant inputs for one implemented opening scenario. The scenario algorithm
    /// remains code-owned; these values and its optional infiltrator override are mod-owned data.
    /// </summary>
    public sealed class ScenarioProfile
    {
        public string Key { get; }
        public string ScenarioKey { get; }
        public int PrimaryFactionId { get; }
        public double PrimarySelectionWeight { get; }
        public long MaxPromisedWorldPopulation { get; }
        public int MinInvaderRegions { get; }
        public int MaxInvaderRegions { get; }
        public float InvaderGarrisonStrengthMultiple { get; }
        public double PostLandingTurnsMean { get; }
        public float SectorLordOpinionReward { get; }
        public float SectorLordOpinionPenalty { get; }
        public ScenarioInfiltratorOverride InfiltratorOverride { get; }

        public ScenarioProfile(
            string key,
            string scenarioKey,
            int primaryFactionId,
            double primarySelectionWeight,
            long maxPromisedWorldPopulation,
            int minInvaderRegions,
            int maxInvaderRegions,
            float invaderGarrisonStrengthMultiple,
            double postLandingTurnsMean,
            float sectorLordOpinionReward,
            float sectorLordOpinionPenalty,
            ScenarioInfiltratorOverride infiltratorOverride = null)
        {
            Key = key;
            ScenarioKey = scenarioKey;
            PrimaryFactionId = primaryFactionId;
            PrimarySelectionWeight = primarySelectionWeight;
            MaxPromisedWorldPopulation = maxPromisedWorldPopulation;
            MinInvaderRegions = minInvaderRegions;
            MaxInvaderRegions = maxInvaderRegions;
            InvaderGarrisonStrengthMultiple = invaderGarrisonStrengthMultiple;
            PostLandingTurnsMean = postLandingTurnsMean;
            SectorLordOpinionReward = sectorLordOpinionReward;
            SectorLordOpinionPenalty = sectorLordOpinionPenalty;
            InfiltratorOverride = infiltratorOverride;
        }
    }

    public sealed class ScenarioProfileCatalog
    {
        private readonly IReadOnlyDictionary<string, ScenarioProfile> _profiles;

        public ScenarioProfileCatalog(IEnumerable<ScenarioProfile> profiles)
        {
            Dictionary<string, ScenarioProfile> map = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> primaryAssignments = new(StringComparer.OrdinalIgnoreCase);
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
                string primaryAssignmentKey = string.Join(
                    "\u001f", profile.ScenarioKey, profile.PrimaryFactionId);
                if (!primaryAssignments.Add(primaryAssignmentKey))
                {
                    throw new InvalidOperationException(
                        $"Scenario '{profile.ScenarioKey}' defines more than one profile for "
                        + $"primary faction {profile.PrimaryFactionId}.");
                }
            }
            _profiles = map;
        }

        public IReadOnlyDictionary<string, ScenarioProfile> Profiles => _profiles;

        public IReadOnlyList<ScenarioProfile> GetForScenario(string scenarioKey) =>
            _profiles.Values
                .Where(profile => string.Equals(
                    profile.ScenarioKey, scenarioKey, StringComparison.OrdinalIgnoreCase))
                .OrderBy(profile => profile.PrimaryFactionId)
                .ThenBy(profile => profile.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

        public ScenarioProfile GetRequired(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || !_profiles.TryGetValue(key, out ScenarioProfile profile))
            {
                throw new InvalidOperationException(
                    $"Required scenario profile '{key}' was not found in the rules database.");
            }
            return profile;
        }

        public ScenarioProfile GetRequiredForScenario(string scenarioKey, int primaryFactionId)
        {
            ScenarioProfile profile = GetForScenario(scenarioKey)
                .SingleOrDefault(candidate => candidate.PrimaryFactionId == primaryFactionId);
            if (profile == null)
            {
                throw new InvalidOperationException(
                    $"Required scenario profile for '{scenarioKey}' and primary faction "
                    + $"{primaryFactionId} was not found in the rules database.");
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
