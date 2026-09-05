using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Models.FactionBehaviors;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace OnlyWar.Models
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
        public IReadOnlyList<Models.Soldiers.Ratings.RatingDefinition> RatingDefinitions { get; set; }
        public IReadOnlyList<Models.Soldiers.Ratings.RatingAwardTier> RatingAwardTiers { get; set; }
        public IReadOnlyList<Models.Soldiers.Ratings.RatingConsumerAssignment> RatingConsumerAssignments { get; set; }
        public IReadOnlyList<Models.Soldiers.Ratings.AwardFamilyDefinition> AwardFamilies { get; set; }
        public IReadOnlyList<SkillRoleAssignment> SkillRoleAssignments { get; set; }
        public IReadOnlyList<FactionRoleAssignment> FactionRoleAssignments { get; set; }
        public IReadOnlyList<ScenarioProfile> ScenarioProfiles { get; set; }
        public IReadOnlyList<ScenarioFactionOption> ScenarioFactionOptions { get; set; }
        public IReadOnlyList<FactionPlanetPresenceRule> FactionPlanetPresenceRules { get; set; }
        public IReadOnlyList<ChapterGenerationProfileData> ChapterGenerationProfiles { get; set; }
        public IReadOnlyList<SectorGenerationProfile> SectorGenerationProfiles { get; set; }
        public IReadOnlyList<FactionBehaviorRulesProfile> FactionBehaviorRulesProfiles { get; set; }

    }

}
