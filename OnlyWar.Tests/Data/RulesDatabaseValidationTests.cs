using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Data;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class RulesDatabaseValidationTests
{
    [Fact]
    public void GameRulesData_ConstructsFromShippedDatabaseWithoutThrowing()
    {
        // GameRulesData performs the load-time validation and builds the registries
        // used by production generation and training paths.
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);

        GameRulesData rules = new();

        Assert.NotEmpty(rules.RatingDefinitions);
        Assert.NotEmpty(rules.RatingAwardTiers);
        Assert.NotEmpty(rules.TrainingProfiles);
        Assert.NotNull(rules.SupplyEconomyRules);
        Assert.NotEmpty(rules.SupplyEconomyRules.RequestValuation.ThroughputBands);
        Assert.True(rules.SupplyEconomyRules.RequestValuation.RequisitionPerBattleValueTime > 0);
        Assert.NotNull(rules.PlayerFaction);
        Assert.NotNull(rules.DefaultFaction);
    }

    [Fact]
    public void CharacterTemplates_LoadWeaponOptionsFromShippedDatabase()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        var playerFaction = rules.Factions.Single(faction => faction.IsPlayerFaction);
        var playerTemplates = playerFaction.SoldierTemplates;
        // "Character" is no longer a SoldierTemplate flag (the same template can be a lone
        // specialist in one squad and a bulk instructor slot in another — the Chaplain in HQ
        // Squad vs. the 10th Company HQ); it is a SquadTemplateElement carrying a Command Weapon
        // quota. Collect every element once so the assertions below can look one up per template.
        List<SquadTemplateElement> allElements = playerFaction.SquadTemplates.Values
            .SelectMany(squad => squad.Elements)
            .ToList();

        // Chaplains, the Master of Sanctity, Reclusiarchs and Judiciars all train Axe, which is
        // the Crozius Arcanum's skill — a default nobody is untrained with.
        foreach (int croziusRoleId in new[] { 9, 10, 16, 51 })
        {
            SoldierTemplate template = playerTemplates[croziusRoleId];
            Assert.NotEmpty(template.GetWeaponOptions(CharacterLoadoutService.CommandWeaponGroup));
            SquadTemplateElement element = allElements.First(e => e.SoldierTemplate == template);
            Assert.Equal("Bolt Pistol + Crozius Arcanum", element.DefaultWeapons?.Name);
        }

        // Every option a character can be given has to be one of his own, or the loadout UI
        // would offer a pick that resolution could never produce.
        foreach (SquadTemplateElement element in allElements.Where(
                     e => e.TryGetQuota(CharacterLoadoutService.CommandWeaponGroup, out _)))
        {
            if (element.DefaultWeapons == null) continue;
            Assert.Contains(element.DefaultWeapons, element.GetMenu(CharacterLoadoutService.CommandWeaponGroup));
        }

        // Line troopers stay outside the character system and keep pooled squad allocation.
        Assert.Empty(playerTemplates[18].GetWeaponOptions(CharacterLoadoutService.CommandWeaponGroup));
        // Sergeants are the point of the element-loadout refactor: they now carry a real Command
        // Weapon menu (so the player can customize them individually) even though the weapon they
        // get absent any choice is unchanged (see element defaults, asserted elsewhere).
        Assert.NotEmpty(playerTemplates[12].GetWeaponOptions(CharacterLoadoutService.CommandWeaponGroup));
    }

    [Fact]
    public void PlayerSpecialistTemplates_LoadDataDrivenPromotionRequirements()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        var player = rules.Factions.Single(faction => faction.IsPlayerFaction);
        Dictionary<string, SoldierTemplate> templates =
            player.SoldierTemplates.Values.ToDictionary(template => template.Name);
        HashSet<string> entryRoles =
            ["Apothecary", "Techmarine", "Lexicanium", "Judiciar"];

        List<SoldierTemplate> specialistTemplates = templates.Values
            .Where(template => template.SpecialistType > 0)
            .ToList();
        Assert.All(specialistTemplates, template =>
            Assert.NotEmpty(template.PromotionRequirements));
        Assert.All(specialistTemplates.Where(template => !entryRoles.Contains(template.Name)), template =>
            AssertRequirement(
                template,
                SoldierTemplateRequirementType.CurrentSpecialistType,
                SoldierTemplateRequirementKeys.SpecialistType,
                SoldierTemplateRequirementComparison.Equal,
                template.SpecialistType));

        AssertRequirement(
            templates["Lexicanium"],
            SoldierTemplateRequirementType.SoldierStat,
            SoldierTemplateRequirementKeys.PsychicPower,
            SoldierTemplateRequirementComparison.GreaterThan,
            0);
        AssertRequirement(
            templates["Apothecary"],
            SoldierTemplateRequirementType.Rating,
            RatingKeys.Medical,
            SoldierTemplateRequirementComparison.GreaterThan,
            95);
        AssertRequirement(
            templates["Techmarine"],
            SoldierTemplateRequirementType.Rating,
            RatingKeys.Tech,
            SoldierTemplateRequirementComparison.GreaterThan,
            75);
        AssertRequirement(
            templates["Judiciar"],
            SoldierTemplateRequirementType.Rating,
            RatingKeys.Piety,
            SoldierTemplateRequirementComparison.GreaterThan,
            90);

        AssertRequirement(
            templates["Master of the Apothecarion"],
            SoldierTemplateRequirementType.Rating,
            RatingKeys.Medical,
            SoldierTemplateRequirementComparison.GreaterThan,
            115);
        AssertRequirement(
            templates["Master of the Apothecarion"],
            SoldierTemplateRequirementType.Rating,
            RatingKeys.Leadership,
            SoldierTemplateRequirementComparison.GreaterThan,
            60);
        AssertRequirement(
            templates["Master of the Apothecarion"],
            SoldierTemplateRequirementType.CurrentSpecialistType,
            SoldierTemplateRequirementKeys.SpecialistType,
            SoldierTemplateRequirementComparison.Equal,
            1);
    }

    [Fact]
    public void BodyTemplates_DefinePhysicalHandGroups()
    {
        var rules = RulesDatabaseFixture.LoadRules();

        var marine = rules.Factions
            .Where(faction => faction.Species != null)
            .SelectMany(faction => faction.Species.Values)
            .Single(species => species.Name == "Space Marine");
        var groups = marine.BodyTemplate.HitLocations
            .Where(location => location.HandGroupId.HasValue)
            .GroupBy(location => location.HandGroupId.Value)
            .OrderBy(group => group.Key)
            .ToList();

        Assert.Equal(2, groups.Count);
        Assert.Equal(["Left Arm", "Left Hand"], groups[0].Select(location => location.Name));
        Assert.Equal(["Right Arm", "Right Hand"], groups[1].Select(location => location.Name));
    }

    private static void AssertRequirement(
        SoldierTemplate template,
        SoldierTemplateRequirementType requirementType,
        string requirementKey,
        SoldierTemplateRequirementComparison comparison,
        float requiredValue)
    {
        Assert.Contains(template.PromotionRequirements, requirement =>
            requirement.RequirementType == requirementType
            && requirement.RequirementKey == requirementKey
            && requirement.Comparison == comparison
            && requirement.RequiredValue == requiredValue);
    }

    [Fact]
    public void RangedWeaponTemplates_LoadTemplateWeaponColumnsFromShippedDatabase()
    {
        var rules = RulesDatabaseFixture.LoadRules();

        foreach (int weaponId in new[] { 2, 18 })
        {
            var flamer = rules.RangedWeaponTemplates[weaponId];

            Assert.True(flamer.IsTemplateWeapon);
            Assert.Equal(1, flamer.TemplateType);
            Assert.Equal(3.0f, flamer.AreaRadius);
            Assert.Equal(5, flamer.AmmoCapacity);
            Assert.Equal(1, flamer.FuelPerBurst);
            Assert.Equal(0, flamer.Accuracy);
        }

        var bolter = rules.RangedWeaponTemplates[1];
        Assert.False(bolter.IsTemplateWeapon);
        Assert.Equal(0, bolter.AreaRadius);
        Assert.Equal(0, bolter.FuelPerBurst);
    }

    [Fact]
    public void RangedWeaponTemplates_LoadGrenadeRowsFromShippedDatabase()
    {
        var rules = RulesDatabaseFixture.LoadRules();

        var marineFrag = rules.RangedWeaponTemplates[35];
        var genericFrag = rules.RangedWeaponTemplates[36];
        Assert.Equal("Throwing", marineFrag.RelatedSkill.Name);
        Assert.Equal("Generic Ranged", genericFrag.RelatedSkill.Name);
        foreach (var frag in new[] { marineFrag, genericFrag })
        {
            Assert.Equal("Frag Grenade", frag.Name);
            Assert.Equal(3, frag.TemplateType);
            Assert.True(frag.IsTemplateWeapon);
            Assert.True(frag.IsBlastWeapon);
            Assert.True(frag.IsThrown);
            Assert.False(frag.IsConeWeapon);
            Assert.Equal(6.0f, frag.AreaRadius);
            Assert.Equal(3.0f, frag.MaximumRange); // meters per Strength point
            Assert.Equal(5.0f, frag.DamageMultiplier);
            Assert.Equal(1, frag.RateOfFire);
            Assert.Equal(1, frag.AmmoCapacity);
            Assert.Equal(1, frag.ReloadTime);
            Assert.False(frag.DoesDamageDegradeWithRange);
        }

        var grenadeLauncher = rules.RangedWeaponTemplates[19];
        Assert.Equal(2, grenadeLauncher.TemplateType);
        Assert.True(grenadeLauncher.IsBlastWeapon);
        Assert.False(grenadeLauncher.IsThrown);
        Assert.False(grenadeLauncher.IsConeWeapon);
        Assert.Equal(6.0f, grenadeLauncher.AreaRadius);
        Assert.Equal(6.0f, grenadeLauncher.DamageMultiplier);
        Assert.Equal(1.0f, grenadeLauncher.ArmorMultiplier);
        Assert.Equal(1000.0f, grenadeLauncher.MaximumRange);
    }

    [Fact]
    public void WeaponSets_LoadGrenadeSlotFromShippedDatabase()
    {
        var rules = RulesDatabaseFixture.LoadRules();

        // Every Space Marine set carries the Throwing-skill frag grenade,
        // including the melee-only Eviscerator set (13).
        foreach (int marineSetId in new[] { 0, 1, 13, 14, 37 })
        {
            Assert.Same(rules.RangedWeaponTemplates[35], rules.WeaponSets[marineSetId].GrenadeWeapon);
        }

        // Imperial/PDF and human-tier Genestealer Cult sets carry the generic frag.
        foreach (int genericSetId in new[] { 24, 25, 27, 30, 31 })
        {
            Assert.Same(rules.RangedWeaponTemplates[36], rules.WeaponSets[genericSetId].GrenadeWeapon);
        }

        // Tyranid and non-human sets carry none.
        foreach (int noGrenadeSetId in new[] { 15, 17, 20, 22, 26, 38 })
        {
            Assert.Null(rules.WeaponSets[noGrenadeSetId].GrenadeWeapon);
        }
    }

    [Fact]
    public void SquadTemplates_PermitIndividualDetachment_ForExactlyTheHqSquadsAndChapterOffices()
    {
        // Pins RulesMigration_SpecialistDetachment.sql. The flag is two-sided
        // (Design/Reference/SpecialistAttachment.md §3.3): these eight formations may lend
        // individuals to an order and may never be ordered as squads. Line squads must not
        // carry it, or a tactical squad would silently stop being deployable.
        var rules = RulesDatabaseFixture.LoadRules();
        Dictionary<int, SquadTemplate> allTemplates = rules.Factions
            .Where(faction => faction.SquadTemplates != null)
            .SelectMany(faction => faction.SquadTemplates.Values)
            .ToDictionary(template => template.Id);

        int[] expected = [5, 6, 7, 8, 9, 10, 11, 19];
        int[] actual = allTemplates.Values
            .Where(template => template.PermitsIndividualDetachment)
            .Select(template => template.Id)
            .OrderBy(id => id)
            .ToArray();
        Assert.Equal(expected, actual);

        // Line squads: Veteran, Tactical, Assault, Devastator, Scout.
        foreach (int lineTemplateId in new[] { 0, 1, 2, 3, 4 })
        {
            Assert.False(allTemplates[lineTemplateId].PermitsIndividualDetachment);
        }

        // The flag must not have been implemented as Administrative: IsOperational is
        // load-bearing for surgery staffing and recruitment/implantation.
        Assert.All(expected, id => Assert.True(allTemplates[id].IsOperational));
    }

    [Fact]
    public void ForceGenerator_MobilisesADefendingForceForADefaultFactionGarrison()
    {
        // This drives the end-to-end path a PDF garrison takes when assaulted:
        // ForceGenerator's Garrison profile must produce a usable defending force.
        var rules = RulesDatabaseFixture.LoadRules();
        var pdf = rules.Factions.Single(f => f.IsDefaultFaction);

        const long targetBattleValue = 700;
        List<Squad> force = ForceGenerator.GenerateForce(new ForceGenerationRequest
        {
            Faction = pdf,
            TargetBattleValue = targetBattleValue,
            Profile = ForceCompositionProfile.Garrison
        }, StaticRNG.Instance);

        Assert.NotEmpty(force);
        Assert.All(force, squad =>
        {
            Assert.True((squad.SquadTemplate.SquadType & SquadTypes.HQ) == 0);
            Assert.NotEmpty(squad.Members);
            Assert.True(squad.Members.Sum(member => (long)member.Template.BattleValue) > 0);
        });
        long generatedBattleValue = force.Sum(squad =>
            squad.Members.Sum(member => (long)member.Template.BattleValue));
        Assert.InRange(generatedBattleValue, 1, targetBattleValue);
    }
}
