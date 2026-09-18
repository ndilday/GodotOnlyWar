using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Soldiers.Ratings;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Domain.FactionBehaviors;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace OnlyWar.Domain
{
    public class GameRulesBlob
    {
        public IReadOnlyList<Faction> Factions { get; set; }
        public IReadOnlyDictionary<int, BaseSkill> BaseSkills { get; set; }
        public IReadOnlyList<SkillTemplate> SkillTemplates { get; set; }
        public IReadOnlyDictionary<int, List<HitLocationTemplate>> BodyTemplates { get; set; }
        public IReadOnlyDictionary<int, PlanetTemplate> PlanetTemplates { get; set; }
        public IReadOnlyDictionary<int, RangedWeaponTemplate> RangedWeaponTemplates { get; set; }
        public IReadOnlyDictionary<int, MeleeWeaponTemplate> MeleeWeaponTemplates { get; set; }
        public IReadOnlyDictionary<int, WeaponSet> WeaponSets { get; set; }
        public EquipmentRulesCatalog EquipmentCatalog { get; set; }
        public IReadOnlyDictionary<int, TrainingProfile> TrainingProfiles { get; set; }
        public IReadOnlyList<PlanetTemplateEligibilityAssignment> PlanetTemplateEligibilityAssignments { get; set; }
        public ScoutTrainingOptionCatalog ScoutTrainingOptions { get; set; }
        public IReadOnlyList<RatingDefinition> RatingDefinitions { get; set; }
        public IReadOnlyList<RatingAwardTier> RatingAwardTiers { get; set; }
        public IReadOnlyList<RatingConsumerAssignment> RatingConsumerAssignments { get; set; }
        public IReadOnlyList<AwardFamilyDefinition> AwardFamilies { get; set; }
        public IReadOnlyList<SkillRoleAssignment> SkillRoleAssignments { get; set; }
        public IReadOnlyList<FactionRoleAssignment> FactionRoleAssignments { get; set; }
        public IReadOnlyList<ScenarioProfile> ScenarioProfiles { get; set; }
        public IReadOnlyList<ScenarioInfiltratorOverride> ScenarioInfiltratorOverrides { get; set; }
        public IReadOnlyList<FactionPlanetPresenceRule> FactionPlanetPresenceRules { get; set; }
        public IReadOnlyList<ChapterGenerationProfileData> ChapterGenerationProfiles { get; set; }
        public IReadOnlyList<SectorGenerationProfile> SectorGenerationProfiles { get; set; }
        public IReadOnlyList<FactionBehaviorRulesProfile> FactionBehaviorRulesProfiles { get; set; }
        // One allocation-doctrine row per faction, keyed by faction id. See ForceDoctrineWeights.
        public IReadOnlyDictionary<int, ForceDoctrineWeights> FactionDoctrines { get; set; }

    }

}
