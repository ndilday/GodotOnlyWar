using OnlyWar.Helpers;
using OnlyWar.Helpers.Database.GameRules;
using OnlyWar.Helpers.Storage;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.Supply;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models
{
    internal sealed class GameRulesData
    {
        public bool DebugMode { get; private set; }
        // Sector Data
        // Measured in light years
        public Coordinate SectorSize { get; private set; }
        // measured in pixels
        public Coordinate SectorCellSize { get; private set; }
        // in RAW, 20 light years is the maximum subsector diameter
        public ushort MaxSubsectorCellDiameter { get; private set; }
        // percent chance of a planet in a sector cell
        public float PlanetChance { get; private set; }
        // Battle Data
        public Coordinate BattleCellSize { get; private set; }

        // Mod Data
        private readonly IReadOnlyList<Faction> _factions;
        private readonly IReadOnlyDictionary<int, BaseSkill> _baseSkillMap;
        private readonly IReadOnlyList<SkillTemplate> _skillTemplateList;
        private readonly IReadOnlyDictionary<int, List<HitLocationTemplate>> _bodyHitLocationTemplateMap;
        private readonly IReadOnlyDictionary<int, PlanetTemplate> _planetTemplateMap;
        public IReadOnlyList<Faction> Factions { get => _factions; }
        public Faction PlayerFaction { get; }
        public Faction DefaultFaction { get; }
        public IReadOnlyDictionary<int, BaseSkill> BaseSkillMap { get => _baseSkillMap; }
        public IReadOnlyList<SkillTemplate> SkillTemplateList { get => _skillTemplateList; }
        public IReadOnlyDictionary<int, List<HitLocationTemplate>> BodyHitLocationTemplateMap { get => _bodyHitLocationTemplateMap; }
        public IReadOnlyDictionary<int, PlanetTemplate> PlanetTemplateMap { get => _planetTemplateMap; }
        public IReadOnlyDictionary<int, RangedWeaponTemplate> RangedWeaponTemplates { get; }
        public IReadOnlyDictionary<int, MeleeWeaponTemplate> MeleeWeaponTemplates { get; }
        public IReadOnlyDictionary<int, WeaponSet> WeaponSets { get; }
        public IReadOnlyDictionary<int, TrainingProfile> TrainingProfiles { get; }
        public IReadOnlyList<RatingDefinition> RatingDefinitions { get; }
        public IReadOnlyList<RatingAwardTier> RatingAwardTiers { get; }
        public SupplyEconomyRules SupplyEconomyRules { get; }

        // Validated registry of base skills that game logic references by name
        // (see TDD §8.3). Resolved and validated at load; fails fast if missing.
        public NamedSkillRegistry Skills { get; }

        // Validated registry of the player-faction soldier/squad templates that
        // chapter generation references by name (see TDD §8.3). Resolved and
        // validated at load; fails fast if missing.
        public ChapterGenerationTemplates ChapterTemplates { get; }

        // Validated registry of the non-player factions that sector generation
        // places by name (see TDD §8.3 / §4.1.1). Resolved and validated at load;
        // fails fast if missing.
        public SectorGenerationFactions SectorFactions { get; }

        public GameRulesData(string databasePath = null)
        {
            databasePath ??= GameStorage.RulesDatabasePath;
            var gameBlob = GameRulesDataAccess.Instance.GetData(databasePath);
            
            DebugMode = true;
            SectorSize = new(200, 200);
            SectorCellSize = new(20, 20);
            MaxSubsectorCellDiameter = 20;
            PlanetChance = 0.02f;
            BattleCellSize = new(20, 20);

            _factions = gameBlob.Factions;
            _baseSkillMap = gameBlob.BaseSkills;
            _skillTemplateList = gameBlob.SkillTemplates;
            _bodyHitLocationTemplateMap = gameBlob.BodyTemplates;
            _planetTemplateMap = gameBlob.PlanetTemplates;
            RangedWeaponTemplates = gameBlob.RangedWeaponTemplates;
            MeleeWeaponTemplates = gameBlob.MeleeWeaponTemplates;
            WeaponSets = gameBlob.WeaponSets;
            TrainingProfiles = gameBlob.TrainingProfiles;
            RatingDefinitions = gameBlob.RatingDefinitions;
            RatingAwardTiers = gameBlob.RatingAwardTiers;
            SupplyEconomyRules = gameBlob.SupplyEconomyRules;
            PlayerFaction = _factions.First(f => f.IsPlayerFaction);
            DefaultFaction = _factions.First(f => f.IsDefaultFaction);
            Skills = new NamedSkillRegistry(_baseSkillMap);
            ChapterTemplates = new ChapterGenerationTemplates(PlayerFaction);
            SectorFactions = new SectorGenerationFactions(_factions);
            ValidateTrainingSkills();
            ValidateRatingDefinitions();
        }

        // Test hook: shrinks the generated sector so tests that need a real
        // SectorBuilder.GenerateSector run (e.g. save/load round trips) don't pay for the
        // full 200x200 / ~800-planet production sector. Keep the grid large enough relative
        // to MaxSubsectorCellDiameter that not every planet becomes a governance capital,
        // or ScenarioBuilder.SelectPromisedWorld can run out of eligible worlds.
        internal void OverrideSectorGeometryForTesting(Coordinate sectorSize, float planetChance)
        {
            SectorSize = sectorSize;
            PlanetChance = planetChance;
        }

        // Fail fast at load if the rules database is missing any base skill the training
        // logic still references by name (work-experience / scout training); see TDD §8.3.
        private void ValidateTrainingSkills()
        {
            HashSet<string> skillNames = _baseSkillMap.Values.Select(s => s.Name).ToHashSet();
            List<string> missing = SoldierTrainingCalculator.RequiredSkillNames
                .Where(name => !skillNames.Contains(name))
                .ToList();
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "Rules database is missing base skills required by the training logic: "
                    + string.Join(", ", missing) + ".");
            }
        }

        // Fail fast at load if a data-driven rating definition is malformed: every
        // required rating key must be present, each definition must have at least one
        // component, every skill-total component must reference a real base skill, and
        // every award tier must reference an existing rating (Design/DataDrivenRatings.md).
        private void ValidateRatingDefinitions()
        {
            string[] requiredKeys =
            {
                RatingKeys.Melee, RatingKeys.Ranged, RatingKeys.Leadership, RatingKeys.Ancient,
                RatingKeys.Medical, RatingKeys.Tech, RatingKeys.Piety
            };
            HashSet<string> presentKeys = RatingDefinitions.Select(d => d.Key).ToHashSet();
            List<string> missingKeys = requiredKeys.Where(k => !presentKeys.Contains(k)).ToList();
            if (missingKeys.Count > 0)
            {
                throw new InvalidOperationException(
                    "Rules database is missing required rating definitions: "
                    + string.Join(", ", missingKeys) + ".");
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
            }
        }

        public IReadOnlyList<Faction> GetNonPlayerFactions()
        {
            return _factions.Where(f => !f.IsPlayerFaction).ToList();
        }
    }
}
