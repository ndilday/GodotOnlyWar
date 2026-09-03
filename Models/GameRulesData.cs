using OnlyWar.Helpers;
using OnlyWar.Helpers.Database.GameRules;
using OnlyWar.Helpers.Storage;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Supply;
using OnlyWar.Models.Units;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Orks;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models
{
    internal sealed class GameRulesData
    {
        public bool DebugMode { get; private set; }
        public SectorGenerationProfile SectorGenerationProfile { get; private set; }

        // Mod Data
        private readonly IReadOnlyList<Faction> _factions;
        private readonly IReadOnlyDictionary<int, BaseSkill> _baseSkillMap;
        private readonly IReadOnlyList<SkillTemplate> _skillTemplateList;
        private readonly IReadOnlyDictionary<int, List<HitLocationTemplate>> _bodyHitLocationTemplateMap;
        private readonly IReadOnlyDictionary<int, PlanetTemplate> _planetTemplateMap;
        public IReadOnlyList<Faction> Factions { get => _factions; }
        public Faction PlayerFaction { get; }
        public Faction DefaultFaction { get; }
        public FactionBehaviorRulesProfile FactionBehaviorRules { get; }
        public IReadOnlyDictionary<string, FactionBehaviorRulesProfile> FactionBehaviorRulesProfiles { get; }
        internal UnitTemplate StrategicCommandUnitTemplate { get; }
        // Legacy accessors are compatibility projections for old scenario/test content. New
        // production behavior resolves factions through FactionCapabilities and uses the generic
        // rules profile above.
        [Obsolete("Resolve factions through FactionCapabilities.")]
        public Faction OrkFaction => FactionCapabilities.WithCapability(
            _factions, FactionBehavior.GeneratesInvasions).FirstOrDefault();
        [Obsolete("Use FactionBehaviorRules.")]
        public OrkCampaignRulesProfile OrkCampaignRules =>
            FactionBehaviorRules as OrkCampaignRulesProfile;
        [Obsolete("Use FactionBehaviorRules.")]
        public FactionBehaviorRulesProfile OrkInfestationRulesProfile => FactionBehaviorRules;
        [Obsolete("Use StrategicCommandUnitTemplate.")]
        internal UnitTemplate OrkCommandUnitTemplate => StrategicCommandUnitTemplate;
        public IReadOnlyDictionary<int, BaseSkill> BaseSkillMap { get => _baseSkillMap; }
        public IReadOnlyList<SkillTemplate> SkillTemplateList { get => _skillTemplateList; }
        public IReadOnlyDictionary<int, List<HitLocationTemplate>> BodyHitLocationTemplateMap { get => _bodyHitLocationTemplateMap; }
        public IReadOnlyDictionary<int, PlanetTemplate> PlanetTemplateMap { get => _planetTemplateMap; }
        public IReadOnlyDictionary<int, RangedWeaponTemplate> RangedWeaponTemplates { get; }
        public IReadOnlyDictionary<int, MeleeWeaponTemplate> MeleeWeaponTemplates { get; }
        public IReadOnlyDictionary<int, WeaponSet> WeaponSets { get; }
        public EquipmentRulesCatalog EquipmentCatalog { get; }
        public IReadOnlyDictionary<int, EquipmentTemplate> EquipmentTemplates => EquipmentCatalog?.EquipmentTemplates;
        public IReadOnlyDictionary<int, EquipmentKitTemplate> EquipmentKits => EquipmentCatalog?.EquipmentKits;
        public IReadOnlyDictionary<int, AmmunitionType> AmmunitionTypes => EquipmentCatalog?.AmmunitionTypes;
        public IReadOnlyDictionary<int, PersonalEquipmentRole> PersonalEquipmentRoles => EquipmentCatalog?.PersonalEquipmentRoles;
        public IReadOnlyDictionary<int, TrainingProfile> TrainingProfiles { get; }
        public ScoutTrainingOptionCatalog ScoutTrainingOptions { get; }
        public PlanetTemplateEligibilityCatalog PlanetTemplateEligibility { get; }
        public IReadOnlyList<RatingDefinition> RatingDefinitions { get; }
        public IReadOnlyList<RatingAwardTier> RatingAwardTiers { get; }
        public RatingConsumerBindings RatingConsumers { get; }
        public AwardFamilyCatalog AwardCatalog { get; }
        public SupplyEconomyRules SupplyEconomyRules { get; }
        public ScenarioProfileCatalog ScenarioProfiles { get; }
        public FactionPlanetPresenceCatalog FactionPlanetPresence { get; }
        // Validated registry of code-owned skill roles resolved through stable rules-data keys
        // (see TDD §8.3). Resolved and validated at load; fails fast if missing.
        public NamedSkillRegistry Skills { get; }

        // The active, validated chapter-generation doctrine. It is compiled from a
        // rules-database profile and exposes semantic role bindings to consumers.
        public ChapterGenerationDoctrine ChapterDoctrine { get; }
        public IReadOnlyDictionary<string, ChapterGenerationDoctrine> ChapterDoctrines { get; }

        // Validated registry of the faction roles that sector generation uses. Resolved from
        // data-owned role assignments at load; fails fast if missing.
        public SectorGenerationFactions SectorFactions { get; }

        public GameRulesData(string databasePath = null)
        {
            databasePath ??= GameStorage.RulesDatabasePath;
            var gameBlob = GameRulesDataAccess.Instance.GetData(databasePath);
            
            DebugMode = true;
            SectorGenerationProfile = ResolveDefaultSectorGenerationProfile(
                gameBlob.SectorGenerationProfiles);

            _factions = gameBlob.Factions;
            _baseSkillMap = gameBlob.BaseSkills;
            _skillTemplateList = gameBlob.SkillTemplates;
            _bodyHitLocationTemplateMap = gameBlob.BodyTemplates;
            _planetTemplateMap = gameBlob.PlanetTemplates;
            RangedWeaponTemplates = gameBlob.RangedWeaponTemplates;
            MeleeWeaponTemplates = gameBlob.MeleeWeaponTemplates;
            WeaponSets = gameBlob.WeaponSets;
            EquipmentCatalog = gameBlob.EquipmentCatalog;
            TrainingProfiles = gameBlob.TrainingProfiles;
            PlanetTemplateEligibility = new PlanetTemplateEligibilityCatalog(
                gameBlob.PlanetTemplateEligibilityAssignments);
            ScoutTrainingOptions = gameBlob.ScoutTrainingOptions;
            RatingDefinitions = gameBlob.RatingDefinitions;
            RatingAwardTiers = gameBlob.RatingAwardTiers;
            RatingConsumers = new RatingConsumerBindings(
                gameBlob.RatingConsumerAssignments
                ?? RatingConsumerBindings.CreateDefaultAssignments());
            AwardCatalog = new AwardFamilyCatalog(
                gameBlob.AwardFamilies ?? AwardFamilyCatalog.CreateDefault().Families.Values);
            SupplyEconomyRules = SupplyEconomyRules.CreateDefault();
            PlayerFaction = ResolveExactlyOneFaction(
                faction => faction.IsPlayerFaction, "player faction");
            DefaultFaction = ResolveExactlyOneFaction(
                faction => faction.IsDefaultFaction, "default faction");
            ValidateBaseSkillKeys();
            Skills = new NamedSkillRegistry(_baseSkillMap, gameBlob.SkillRoleAssignments);
            ChapterDoctrines = CompileChapterDoctrines(gameBlob.ChapterGenerationProfiles);
            ChapterDoctrine = ResolveDefaultChapterDoctrine(PlayerFaction, ChapterDoctrines);
            SectorFactions = new SectorGenerationFactions(
                _factions, gameBlob.FactionRoleAssignments);
            ScenarioProfiles = new ScenarioProfileCatalog(gameBlob.ScenarioProfiles);
            FactionPlanetPresence = new FactionPlanetPresenceCatalog(
                gameBlob.FactionPlanetPresenceRules);
            FactionBehaviorRulesProfiles = ResolveFactionBehaviorRulesProfiles(
                gameBlob.FactionBehaviorRulesProfiles);
            FactionBehaviorRules = FactionBehaviorRulesProfiles.Values.First() is OrkCampaignRulesProfile legacyProfile
                ? legacyProfile
                : new OrkCampaignRulesProfile(FactionBehaviorRulesProfiles.Values.First());
            StrategicCommandUnitTemplate = EnsureStrategicCommandUnitTemplate();
            ValidateFactionGenerationPolicies(gameBlob.ScenarioFactionOptions);
            ValidateRatingDefinitions();
            ValidateSoldierTemplateRequirements();
        }

        private static IReadOnlyDictionary<string, FactionBehaviorRulesProfile>
            ResolveFactionBehaviorRulesProfiles(IReadOnlyList<FactionBehaviorRulesProfile> profiles)
        {
            if (profiles == null || profiles.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Rules database must define at least one faction behavior profile; found {profiles?.Count ?? 0}.");
            }
            Dictionary<string, FactionBehaviorRulesProfile> result =
                new(StringComparer.OrdinalIgnoreCase);
            foreach (FactionBehaviorRulesProfile profile in profiles)
            {
                if (profile == null) throw new InvalidOperationException(
                    "Rules database contains a null faction behavior profile.");
                profile.Validate();
                if (!result.TryAdd(profile.Key.Trim(), profile))
                {
                    throw new InvalidOperationException(
                        $"Rules database contains duplicate faction behavior profile '{profile.Key}'.");
                }
            }
            return result;
        }

        private Faction ResolveExactlyOneFaction(
            Func<Faction, bool> predicate,
            string roleName)
        {
            List<Faction> matches = _factions.Where(predicate).ToList();
            if (matches.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Rules database must define exactly one {roleName}; found {matches.Count}.");
            }
            return matches[0];
        }

        private UnitTemplate EnsureStrategicCommandUnitTemplate()
        {
            Faction faction = FactionCapabilities.WithCapability(
                _factions, FactionBehavior.GeneratesInvasions).FirstOrDefault();
            if (faction == null) return null;

            SquadTemplate commandTemplate = faction.SquadTemplates?.Values
                .Where(candidate => candidate.BattleValue > 0
                    && candidate.SquadType.HasFlag(SquadTypes.HQ))
                .OrderByDescending(candidate => candidate.Elements
                    .Count(element => element.SoldierTemplate?.IsSquadLeader == true))
                .ThenByDescending(candidate => candidate.BattleValue)
                .ThenBy(candidate => candidate.Id)
                .FirstOrDefault();
            if (commandTemplate == null) return null;

            UnitTemplate existing = faction.UnitTemplates?.Values
                .FirstOrDefault(candidate => candidate.HQSquad == commandTemplate);
            if (existing != null) return existing;

            const int runtimeTemplateId = -1700001;
            existing = faction.UnitTemplates?.GetValueOrDefault(runtimeTemplateId);
            if (existing != null) return existing;

            UnitTemplate generated = new(
                runtimeTemplateId,
                "Strategic Invasion Warband",
                false,
                commandTemplate,
                []);
            faction.AddRuntimeUnitTemplate(generated);
            return generated;
        }

        private static SectorGenerationProfile ResolveDefaultSectorGenerationProfile(
            IReadOnlyList<SectorGenerationProfile> profiles)
        {
            List<SectorGenerationProfile> defaults = (profiles ?? [])
                .Where(profile => profile?.IsDefault == true)
                .ToList();
            if (defaults.Count != 1)
            {
                throw new InvalidOperationException(
                    "Rules database must define exactly one default sector generation profile; "
                    + $"found {defaults.Count}.");
            }
            return defaults[0];
        }

        private IReadOnlyDictionary<string, ChapterGenerationDoctrine> CompileChapterDoctrines(
            IReadOnlyList<ChapterGenerationProfileData> profiles)
        {
            if (profiles == null || profiles.Count == 0)
            {
                throw new InvalidOperationException(
                    "Rules database must define at least one chapter generation profile.");
            }

            Dictionary<string, ChapterGenerationDoctrine> doctrines =
                new(StringComparer.OrdinalIgnoreCase);
            foreach (ChapterGenerationProfileData profile in profiles)
            {
                Faction faction = _factions.SingleOrDefault(item => item.Id == profile.FactionId);
                if (faction == null)
                {
                    throw new InvalidOperationException(
                        $"Chapter generation profile '{profile.ProfileKey}' references missing faction "
                        + $"id {profile.FactionId}.");
                }
                string profileKey = profile.ProfileKey?.Trim();
                if (string.IsNullOrWhiteSpace(profileKey))
                {
                    throw new InvalidOperationException(
                        "Rules database contains a chapter generation profile with no key.");
                }
                if (doctrines.ContainsKey(profileKey))
                {
                    throw new InvalidOperationException(
                        $"Rules database contains duplicate chapter generation profile '{profileKey}'.");
                }
                doctrines.Add(profileKey, new ChapterGenerationDoctrine(faction, profile));
            }
            return doctrines;
        }

        private static ChapterGenerationDoctrine ResolveDefaultChapterDoctrine(
            Faction faction,
            IReadOnlyDictionary<string, ChapterGenerationDoctrine> doctrines)
        {
            List<ChapterGenerationDoctrine> matches = doctrines.Values
                .Where(doctrine => doctrine.RootUnit.Faction == faction)
                .Where(doctrine => doctrine.IsDefault)
                .ToList();
            if (matches.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Rules database must define exactly one default chapter generation profile "
                    + $"for faction '{faction.Name}'; found {matches.Count}.");
            }
            return matches[0];
        }

        internal ChapterGenerationDoctrine GetPlayerChapterDoctrine(string profileKey = null)
        {
            if (string.IsNullOrWhiteSpace(profileKey))
            {
                return ChapterDoctrine;
            }

            string normalizedProfileKey = profileKey.Trim();
            if (!ChapterDoctrines.TryGetValue(normalizedProfileKey, out ChapterGenerationDoctrine doctrine))
            {
                throw new InvalidOperationException(
                    $"Chapter generation profile '{normalizedProfileKey}' was not found.");
            }
            if (doctrine.RootUnit.Faction != PlayerFaction)
            {
                throw new InvalidOperationException(
                    $"Chapter generation profile '{normalizedProfileKey}' does not belong to the "
                    + "player faction.");
            }
            return doctrine;
        }

        private void ValidateFactionGenerationPolicies(
            IReadOnlyList<ScenarioFactionOption> scenarioOptions)
        {
            ValidateSectorFactionRoles();
            ValidateScenarioProfiles();
            ValidateScenarioFactionOptions(scenarioOptions);
            ValidateFactionPlanetPresenceRules();
        }

        private void ValidateScenarioProfiles()
        {
            foreach (ScenarioProfile profile in ScenarioProfiles.Profiles.Values)
            {
                if (profile.MaxPromisedWorldPopulation <= 0
                    || profile.MinInvaderRegions <= 0
                    || profile.MaxInvaderRegions < profile.MinInvaderRegions
                    || profile.PreLandingTurns < 0
                    || !IsNonNegativeFinite(profile.InvaderGarrisonStrengthMultiple)
                    || !IsUnitInterval(profile.ImperialRemnantFraction)
                    || !IsUnitInterval(profile.InitialInfiltratorPopulationShareMin)
                    || !IsUnitInterval(profile.InitialInfiltratorPopulationShareMax)
                    || profile.InitialInfiltratorPopulationShareMin
                        > profile.InitialInfiltratorPopulationShareMax
                    || !IsNonNegativeFinite(profile.InitialInfiltratorGarrisonPerPopulation)
                    || !IsUnitInterval(profile.PromisedWorldInfiltratorStrengthFraction)
                    || !IsNonNegativeFinite(profile.PromisedWorldInfiltratorStartingIntel)
                    || !IsNonNegativeFinite(profile.PostLandingTurnsMean)
                    || !IsNonNegativeFinite(profile.SectorLordOpinionReward)
                    || !IsNonNegativeFinite(profile.SectorLordOpinionPenalty))
                {
                    throw new InvalidOperationException(
                        $"Scenario profile '{profile.Key}' has invalid balance or timing values.");
                }
            }
        }

        private void ValidateSectorFactionRoles()
        {
            if (SectorFactions.Infiltrator.Id == SectorFactions.Invader.Id
                || SectorFactions.Infiltrator.Id == SectorFactions.Insurrectionists.Id
                || SectorFactions.Invader.Id == SectorFactions.Insurrectionists.Id)
            {
                throw new InvalidOperationException(
                    "Sector faction roles must resolve to distinct factions.");
            }

            if (!SectorFactions.Infiltrator.HasBehavior(FactionBehavior.CanInfiltrate))
            {
                throw new InvalidOperationException(
                    $"Faction '{SectorFactions.Infiltrator.Name}' assigned to "
                    + $"'{FactionRoleKeys.Infiltrator}' must have CanInfiltrate behavior.");
            }
            if (!SectorFactions.Invader.HasBehavior(FactionBehavior.InvadesOnVictory))
            {
                throw new InvalidOperationException(
                    $"Faction '{SectorFactions.Invader.Name}' assigned to "
                    + $"'{FactionRoleKeys.Invader}' must have InvadesOnVictory behavior.");
            }
            if (SectorFactions.Insurrectionists.GrowthType != GrowthType.Unrest)
            {
                throw new InvalidOperationException(
                    $"Faction '{SectorFactions.Insurrectionists.Name}' assigned to "
                    + $"'{FactionRoleKeys.Insurrectionists}' must use Unrest growth.");
            }
        }

        private void ValidateScenarioFactionOptions(
            IReadOnlyList<ScenarioFactionOption> rawOptions)
        {
            HashSet<string> profileKeys = ScenarioProfiles.Profiles.Keys
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            HashSet<(string ScenarioKey, string SlotKey, int FactionId)> seen = [];

            foreach (ScenarioFactionOption option in rawOptions ?? [])
            {
                if (option == null)
                {
                    throw new InvalidOperationException("A scenario faction option is null.");
                }
                if (string.IsNullOrWhiteSpace(option.ScenarioKey)
                    || !profileKeys.Contains(option.ScenarioKey))
                {
                    throw new InvalidOperationException(
                        $"Scenario faction option references unknown scenario "
                        + $"'{option.ScenarioKey}'.");
                }
                ScenarioProfile optionProfile = ScenarioProfiles.GetRequired(option.ScenarioKey);
                if (!ScenarioFactionSlotKeys.TryParse(option.SlotKey, out string slotKey))
                {
                    throw new InvalidOperationException(
                        $"Unknown scenario faction slot '{option.SlotKey}'.");
                }
                if (!seen.Add((optionProfile.Key, slotKey, option.FactionId)))
                {
                    throw new InvalidOperationException(
                        $"Scenario faction option '{option.ScenarioKey}/{option.SlotKey}' "
                        + $"assigns faction id {option.FactionId} more than once.");
                }
                if (!_factions.Any(faction => faction.Id == option.FactionId))
                {
                    throw new InvalidOperationException(
                        $"Scenario faction option '{option.ScenarioKey}/{option.SlotKey}' "
                        + $"references missing faction id {option.FactionId}.");
                }
                if (double.IsNaN(option.SelectionWeight)
                    || double.IsInfinity(option.SelectionWeight)
                    || option.SelectionWeight <= 0)
                {
                    throw new InvalidOperationException(
                        $"Scenario faction option '{option.ScenarioKey}/{option.SlotKey}' "
                        + "must have a positive selection weight.");
                }

                Faction faction = _factions.Single(candidate => candidate.Id == option.FactionId);
                if (slotKey.Equals(ScenarioFactionSlotKeys.Infiltrator, StringComparison.OrdinalIgnoreCase)
                    && !faction.HasBehavior(FactionBehavior.CanInfiltrate))
                {
                    throw new InvalidOperationException(
                        $"Faction '{faction.Name}' assigned to scenario infiltrator slot "
                        + "must have CanInfiltrate behavior.");
                }
                if (slotKey.Equals(ScenarioFactionSlotKeys.Invader, StringComparison.OrdinalIgnoreCase)
                    && (!faction.HasBehavior(FactionBehavior.InvadesOnVictory)
                        || faction.IsPlayerFaction
                        || faction.IsDefaultFaction))
                {
                    throw new InvalidOperationException(
                        $"Faction '{faction.Name}' assigned to scenario invader slot "
                        + "must be a non-hostile-role faction with InvadesOnVictory behavior.");
                }
            }

            ScenarioProfile promisedWorld = ScenarioProfiles.GetRequired(ScenarioKeys.PromisedWorld);
            ValidateRequiredScenarioSlot(promisedWorld, ScenarioFactionSlotKeys.Infiltrator);
            ValidateRequiredScenarioSlot(promisedWorld, ScenarioFactionSlotKeys.Invader);
        }

        private static void ValidateRequiredScenarioSlot(
            ScenarioProfile profile,
            string slotKey)
        {
            IReadOnlyList<ScenarioFactionOption> options = profile.GetFactionOptions(slotKey);
            if (options.Count == 0 || !options.Any(option => option.IsRequired))
            {
                throw new InvalidOperationException(
                    $"Scenario profile '{profile.Key}' requires a faction option for slot '{slotKey}'.");
            }
        }

        private void ValidateFactionPlanetPresenceRules()
        {
            List<FactionPlanetPresenceRule> rules = FactionPlanetPresence.Rules.ToList();
            if (!rules.Any(rule => string.Equals(
                    rule?.ProfileKey,
                    SectorGenerationProfileKeys.Standard,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Rules database must define at least one '{SectorGenerationProfileKeys.Standard}' "
                    + "faction planet-presence rule.");
            }
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (FactionPlanetPresenceRule rule in rules)
            {
                if (rule == null)
                {
                    throw new InvalidOperationException("A faction planet-presence rule is null.");
                }
                if (string.IsNullOrWhiteSpace(rule.ProfileKey))
                {
                    throw new InvalidOperationException(
                        "A faction planet-presence rule has no profile key.");
                }
                if (!string.Equals(
                        rule.ProfileKey,
                        SectorGenerationProfileKeys.Standard,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Faction planet-presence rule references unknown profile "
                        + $"'{rule.ProfileKey}'.");
                }
                string duplicateKey = string.Join(
                    "\u001f",
                    rule.ProfileKey.Trim(),
                    rule.PlanetTemplateId?.ToString() ?? "*",
                    rule.FactionId);
                if (!seen.Add(duplicateKey))
                {
                    throw new InvalidOperationException(
                        $"Faction planet-presence rule '{rule.ProfileKey}' duplicates "
                        + $"faction id {rule.FactionId} for template "
                        + $"'{rule.PlanetTemplateId?.ToString() ?? "*"}'.");
                }
                if (!_factions.Any(faction => faction.Id == rule.FactionId))
                {
                    throw new InvalidOperationException(
                        $"Faction planet-presence rule '{rule.ProfileKey}' references "
                        + $"missing faction id {rule.FactionId}.");
                }
                if (rule.PlanetTemplateId.HasValue
                    && !_planetTemplateMap.ContainsKey(rule.PlanetTemplateId.Value))
                {
                    throw new InvalidOperationException(
                        $"Faction planet-presence rule '{rule.ProfileKey}' references "
                        + $"missing planet template id {rule.PlanetTemplateId.Value}.");
                }
                if (!Enum.IsDefined(rule.PresenceMode))
                {
                    throw new InvalidOperationException(
                        $"Faction planet-presence rule '{rule.ProfileKey}' has unknown "
                        + $"presence mode {(int)rule.PresenceMode}.");
                }
                if (!IsUnitInterval(rule.SpawnChance)
                    || !IsUnitInterval(rule.PopulationShareMin)
                    || !IsUnitInterval(rule.PopulationShareMax)
                    || rule.PopulationShareMin > rule.PopulationShareMax
                    || double.IsNaN(rule.GarrisonPerPopulation)
                    || double.IsInfinity(rule.GarrisonPerPopulation)
                    || rule.GarrisonPerPopulation < 0)
                {
                    throw new InvalidOperationException(
                        $"Faction planet-presence rule '{rule.ProfileKey}' has invalid "
                        + "chance or population distribution values.");
                }
            }

            foreach (string profileKey in rules.Select(rule => rule.ProfileKey)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (int templateId in _planetTemplateMap.Keys)
                {
                    int publicRuleCount = FactionPlanetPresence
                        .GetApplicableRules(profileKey, templateId)
                        .Where(rule => rule.PresenceMode == FactionPresenceMode.Public)
                        .Select(rule => rule.FactionId)
                        .Distinct()
                        .Count();
                    if (publicRuleCount > 1)
                    {
                        throw new InvalidOperationException(
                            $"Faction planet-presence profile '{profileKey}' has multiple "
                            + $"public start rules applicable to planet template {templateId}.");
                    }
                }
            }
        }

        private static bool IsUnitInterval(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 1;

        private static bool IsNonNegativeFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;

        // Test hook: shrinks the generated sector so tests that need a real
        // SectorBuilder.GenerateSector run (e.g. save/load round trips) don't pay for the
        // full 200x200 / ~800-planet production sector. Keep the grid large enough relative
        // to MaxSubsectorDiameter that not every planet becomes a governance capital,
        // or ScenarioBuilder.SelectPromisedWorld can run out of eligible worlds.
        internal void OverrideSectorGeometryForTesting(Coordinate sectorSize, float planetChance)
        {
            SectorGenerationProfile = new SectorGenerationProfile(
                SectorGenerationProfile.Key,
                sectorSize.X,
                sectorSize.Y,
                planetChance,
                SectorGenerationProfile.MaxSubsectorDiameter,
                SectorGenerationProfile.IsDefault);
        }

        // Every rules skill gets a stable identity. Gameplay roles may then point at these keys,
        // while Name remains presentation text that a mod can rename or localize.
        private void ValidateBaseSkillKeys()
        {
            List<int> missing = _baseSkillMap.Values
                .Where(skill => string.IsNullOrWhiteSpace(skill.SkillKey))
                .Select(skill => skill.Id)
                .ToList();
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "Rules database has base skills without a stable SkillKey: "
                    + string.Join(", ", missing) + ".");
            }

            List<string> duplicateKeys = _baseSkillMap.Values
                .GroupBy(skill => skill.SkillKey, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();
            if (duplicateKeys.Count > 0)
            {
                throw new InvalidOperationException(
                    "Rules database contains duplicate base skill keys: "
                    + string.Join(", ", duplicateKeys) + ".");
            }
        }

        // Fail fast at load if a data-driven rating definition is malformed: every
        // required consumer role must be assigned to a real rating, each definition
        // must have at least one component, every skill-total component must reference
        // a real base skill, and every award tier must reference an existing rating.
        private void ValidateRatingDefinitions()
        {
            HashSet<string> presentKeys = RatingDefinitions
                .Select(d => d.Key)
                .ToHashSet(StringComparer.Ordinal);
            List<string> missingConsumerRatings = RatingConsumerBindings.GetRequiredRoles()
                .Where(role => !RatingConsumers.TryGetRatingKey(role, out string key)
                               || !presentKeys.Contains(key))
                .Select(role => RatingConsumerRoleKeys.For(role))
                .ToList();
            if (missingConsumerRatings.Count > 0)
            {
                throw new InvalidOperationException(
                    "Rules database has unfulfilled rating consumer roles: "
                    + string.Join(", ", missingConsumerRatings) + ".");
            }

            List<string> duplicateKeys = RatingDefinitions
                .GroupBy(d => d.Key, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();
            if (duplicateKeys.Count > 0)
            {
                throw new InvalidOperationException(
                    "Rules database contains duplicate rating keys: "
                    + string.Join(", ", duplicateKeys) + ".");
            }

            foreach (RatingDefinition definition in RatingDefinitions)
            {
                if (definition.Components.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Rating definition '{definition.Key}' has no components.");
                }
                foreach (RatingComponent component in definition.Components)
                {
                    if (!Enum.IsDefined(component.ComponentType))
                    {
                        throw new InvalidOperationException(
                            $"Rating definition '{definition.Key}' has unknown component type "
                            + $"{(int)component.ComponentType}.");
                    }
                    if (component.ComponentType == RatingComponentType.SkillTotal
                        && !_baseSkillMap.ContainsKey(component.TargetId))
                    {
                        throw new InvalidOperationException(
                            $"Rating definition '{definition.Key}' references base skill id "
                            + $"{component.TargetId}, which is not in the rules database.");
                    }
                }
            }

            foreach (RatingAwardTier tier in RatingAwardTiers)
            {
                if (!presentKeys.Contains(tier.RatingKey))
                {
                    throw new InvalidOperationException(
                        $"Rating award tier {tier.Id} references rating '{tier.RatingKey}', "
                        + "which has no definition.");
                }
                if (!Enum.IsDefined(tier.Effect))
                {
                    throw new InvalidOperationException(
                        $"Rating award tier {tier.Id} has unknown effect "
                        + $"{(int)tier.Effect}.");
                }
                if (tier.Effect == RatingAwardEffect.Award
                    && string.IsNullOrWhiteSpace(tier.AwardFamilyKey))
                {
                    throw new InvalidOperationException(
                        $"Rating award tier {tier.Id} grants an award without an award family.");
                }
            }
        }

        private void ValidateSoldierTemplateRequirements()
        {
            HashSet<string> ratingKeys = RatingDefinitions.Select(d => d.Key).ToHashSet();
            foreach (SoldierTemplate template in _factions
                         .Where(f => f.SoldierTemplates != null)
                         .SelectMany(f => f.SoldierTemplates.Values))
            {
                foreach (SoldierTemplateRequirement requirement in template.PromotionRequirements)
                {
                    if (!Enum.IsDefined(requirement.RequirementType))
                    {
                        throw new InvalidOperationException(
                            $"Soldier template '{template.Name}' has unknown requirement type "
                            + $"{(int)requirement.RequirementType}.");
                    }
                    if (!Enum.IsDefined(requirement.Comparison))
                    {
                        throw new InvalidOperationException(
                            $"Soldier template '{template.Name}' has unknown requirement comparison "
                            + $"{(int)requirement.Comparison}.");
                    }

                    switch (requirement.RequirementType)
                    {
                        case SoldierTemplateRequirementType.SoldierStat:
                            if (requirement.RequirementKey != SoldierTemplateRequirementKeys.PsychicPower)
                            {
                                throw new InvalidOperationException(
                                    $"Soldier template '{template.Name}' references unknown soldier stat "
                                    + $"'{requirement.RequirementKey}'.");
                            }
                            break;
                        case SoldierTemplateRequirementType.Rating:
                            if (!ratingKeys.Contains(requirement.RequirementKey))
                            {
                                throw new InvalidOperationException(
                                    $"Soldier template '{template.Name}' references unknown rating "
                                    + $"'{requirement.RequirementKey}'.");
                            }
                            break;
                        case SoldierTemplateRequirementType.CurrentSpecialistType:
                            if (requirement.RequirementKey != SoldierTemplateRequirementKeys.SpecialistType
                                || requirement.Comparison != SoldierTemplateRequirementComparison.Equal
                                || requirement.RequiredValue <= 0
                                || requirement.RequiredValue > byte.MaxValue
                                || requirement.RequiredValue != MathF.Truncate(requirement.RequiredValue)
                                || requirement.RequiredValue != template.SpecialistType)
                            {
                                throw new InvalidOperationException(
                                    $"Soldier template '{template.Name}' has an invalid specialist-track requirement.");
                            }
                            break;
                    }
                }
            }
        }

        public IReadOnlyList<Faction> GetNonPlayerFactions()
        {
            return _factions.Where(f => !f.IsPlayerFaction).ToList();
        }
    }
}
