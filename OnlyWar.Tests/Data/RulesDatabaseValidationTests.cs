using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Data;

public class RulesDatabaseValidationTests
{
    [Fact]
    public void RulesDatabase_LoadsCoreData()
    {
        var rules = RulesDatabaseFixture.LoadRules();

        Assert.NotEmpty(rules.Factions);
        Assert.NotEmpty(rules.BaseSkills);
        Assert.NotEmpty(rules.SkillTemplates);
        Assert.NotEmpty(rules.BodyTemplates);
        Assert.NotEmpty(rules.PlanetTemplates);
        Assert.NotEmpty(rules.WeaponSets);
        Assert.NotEmpty(rules.TrainingProfiles);
    }

    [Fact]
    public void RulesDatabase_HasExactlyOnePlayerAndDefaultFaction()
    {
        var rules = RulesDatabaseFixture.LoadRules();

        Assert.Single(rules.Factions, f => f.IsPlayerFaction);
        Assert.Single(rules.Factions, f => f.IsDefaultFaction);
    }

    [Fact]
    public void HardcodedSkillNames_UsedByCurrentLogic_ExistInRulesDatabase()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        HashSet<string> skillNames = rules.BaseSkills.Values.Select(s => s.Name).ToHashSet();
        string[] requiredNames =
        [
            "Armory (Small Arms)",
            "Armory (Vehicle)",
            "Axe",
            "Diagnosis",
            "Drive (Bike)",
            "Drive (Rhino)",
            "First Aid",
            "Fist",
            "Gun (Bolter)",
            "Gun (Flamer)",
            "Gun (Plasma)",
            "Gun (Shotgun)",
            "Gun (Sniper)",
            "Gunnery (Bolter)",
            "Gunnery (Laser)",
            "Gunnery (Rocket)",
            "Jump Pack",
            "Leadership",
            "Marine",
            "Pilot (Land Speeder)",
            "Power Armor",
            "Shield",
            "Stealth",
            "Sword",
            "Tactics",
            "Teaching",
            "Theology (Emperor of Man)"
        ];

        foreach (string requiredName in requiredNames)
        {
            Assert.Contains(requiredName, skillNames);
        }
    }

    [Fact]
    public void HardcodedFactionNames_UsedBySectorGeneration_ExistInRulesDatabase()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        HashSet<string> factionNames = rules.Factions.Select(f => f.Name).ToHashSet();

        Assert.Contains("Genestealer Cult", factionNames);
        Assert.Contains("Tyranids", factionNames);
    }

    [Fact]
    public void PlayerFaction_HasTemplatesRequiredByChapterGeneration()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        var playerFaction = rules.Factions.Single(f => f.IsPlayerFaction);
        HashSet<string> soldierTemplateNames = playerFaction.SoldierTemplates.Values.Select(st => st.Name).ToHashSet();
        HashSet<string> squadTemplateNames = playerFaction.SquadTemplates.Values.Select(st => st.Name).ToHashSet();

        string[] requiredSoldierTemplates =
        [
            "Apothecary",
            "Assault Marine",
            "Captain",
            "Chaplain",
            "Chapter Master",
            "Devastator Marine",
            "Master of Sanctity",
            "Scout Marine",
            "Scout Sergeant",
            "Sergeant",
            "Tactical Marine",
            "Techmarine",
            "Veteran"
        ];

        foreach (string requiredName in requiredSoldierTemplates)
        {
            Assert.Contains(requiredName, soldierTemplateNames);
        }

        Assert.Contains("Scout Squad", squadTemplateNames);
        Assert.Contains("Tactical Squad", squadTemplateNames);
        Assert.Contains("Assault Squad", squadTemplateNames);
        Assert.Contains("Devastator Squad", squadTemplateNames);
    }

    [Fact]
    public void TrainingProfiles_ArePopulatedAndAssignedToTrainablePlayerSoldierTemplates()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        HashSet<string> profileNames = rules.TrainingProfiles.Values.Select(tp => tp.Name).ToHashSet();
        var playerFaction = rules.Factions.Single(f => f.IsPlayerFaction);

        string[] requiredProfiles =
        [
            "assault_marine_work",
            "assault_sergeant_work",
            "devastator_marine_work",
            "devastator_sergeant_work",
            "scout_focus_melee",
            "scout_focus_physical",
            "scout_focus_ranged",
            "scout_focus_vehicles",
            "scout_marine_work",
            "scout_sergeant_work",
            "tactical_marine_work",
            "tactical_sergeant_work",
            "veteran_work"
        ];

        foreach (string requiredProfile in requiredProfiles)
        {
            Assert.Contains(requiredProfile, profileNames);
            Assert.NotEmpty(rules.TrainingProfiles.Values.Single(tp => tp.Name == requiredProfile).Entries);
        }

        string[] trainedSoldierTemplates =
        [
            "Assault Marine",
            "Devastator Marine",
            "Scout Marine",
            "Scout Sergeant",
            "Sergeant",
            "Sergeant (A)",
            "Sergeant (D)",
            "Tactical Marine",
            "Veteran"
        ];

        foreach (string templateName in trainedSoldierTemplates)
        {
            Assert.NotNull(playerFaction.SoldierTemplates.Values.Single(st => st.Name == templateName).WorkExperienceTrainingProfile);
        }
    }

    [Fact]
    public void TrainingProfileEntries_HavePositiveWeightsAndResolvedTargets()
    {
        var rules = RulesDatabaseFixture.LoadRules();

        foreach (var profile in rules.TrainingProfiles.Values)
        {
            Assert.All(profile.Entries, entry =>
            {
                Assert.True(entry.Weight > 0);
                Assert.True(entry.Skill != null || entry.Attribute.HasValue);
            });
        }
    }

    [Fact]
    public void SquadTemplates_AreInternallyConsistent()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        var squadTemplates = rules.Factions
            .Where(f => f.SquadTemplates != null)
            .SelectMany(f => f.SquadTemplates.Values)
            .ToList();

        Assert.NotEmpty(squadTemplates);
        foreach (SquadTemplate template in squadTemplates)
        {
            Assert.NotNull(template.Faction);
            Assert.NotEmpty(template.Elements);
            Assert.True(template.Elements.All(e => e.MaximumNumber >= e.MinimumNumber));
            Assert.True(template.Elements.All(e => e.MaximumNumber > 0));
            Assert.NotNull(template.DefaultWeapons);
            Assert.True(template.DefaultWeapons.PrimaryRangedWeapon != null || template.DefaultWeapons.PrimaryMeleeWeapon != null);
            Assert.NotNull(template.Armor);
        }
    }

    [Fact]
    public void BodyTemplates_DefineHitProbabilitiesForEveryStance()
    {
        var rules = RulesDatabaseFixture.LoadRules();

        foreach (var bodyTemplate in rules.BodyTemplates.Values)
        {
            Assert.NotEmpty(bodyTemplate);
            Assert.All(bodyTemplate, hitLocation =>
            {
                Assert.NotNull(hitLocation.HitProbabilityMap);
                Assert.Equal(3, hitLocation.HitProbabilityMap.Length);
            });

            for (int stanceIndex = 0; stanceIndex < 3; stanceIndex++)
            {
                Assert.True(bodyTemplate.Sum(hl => hl.HitProbabilityMap[stanceIndex]) > 0);
            }
        }
    }
}
