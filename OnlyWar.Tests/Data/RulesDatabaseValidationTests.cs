using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Microsoft.Data.Sqlite;
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
        Assert.Equal(
            RatingKeys.Ranged,
            rules.RatingConsumers[RatingConsumerRole.RangedCombat]);
        Assert.Equal(
            "core:honor_gun",
            rules.AwardCatalog.Get(AwardTypes.Gun).IconAssetKey);
        Assert.NotEmpty(rules.TrainingProfiles);
        Assert.Equal(
            [
                ScoutTrainingOptionKeys.Balanced,
                ScoutTrainingOptionKeys.Physical,
                ScoutTrainingOptionKeys.Vehicles,
                ScoutTrainingOptionKeys.Melee,
                ScoutTrainingOptionKeys.Ranged
            ],
            rules.ScoutTrainingOptions.Options.Select(option => option.Key));
        Assert.Same(
            rules.ScoutTrainingOptions.GetRequired(ScoutTrainingOptionKeys.Balanced).Profile,
            rules.TrainingProfiles[
                rules.ScoutTrainingOptions.GetRequired(ScoutTrainingOptionKeys.Balanced)
                    .Profile.Id]);
        Assert.NotNull(rules.SupplyEconomyRules);
        Assert.NotEmpty(rules.SupplyEconomyRules.RequestValuation.ThroughputBands);
        Assert.True(rules.SupplyEconomyRules.RequestValuation.RequisitionPerBattleValueTime > 0);
        Assert.NotEmpty(rules.EquipmentTemplates);
        Assert.NotEmpty(rules.EquipmentKits);
        Assert.NotEmpty(rules.PersonalEquipmentRoles);
        Assert.All(rules.BaseSkillMap.Values, skill => Assert.False(string.IsNullOrWhiteSpace(skill.SkillKey)));
        Assert.Same(
            rules.BaseSkillMap.Values.Single(skill => skill.SkillKey == SkillRoleKeys.Stealth),
            rules.Skills.Stealth);
        Assert.NotNull(rules.PlayerFaction);
        Assert.NotNull(rules.DefaultFaction);
        Assert.NotNull(rules.SectorFactions.Infiltrator);
        Assert.NotNull(rules.SectorFactions.Invader);
        Assert.NotNull(rules.SectorFactions.Insurrectionists);
        ScenarioProfile promisedWorld = rules.ScenarioProfiles
            .GetRequired(ScenarioKeys.PromisedWorld);
        Assert.Equal(2, promisedWorld.MinInvaderRegions);
        Assert.Equal(3, promisedWorld.MaxInvaderRegions);
        Assert.Contains(promisedWorld.GetFactionOptions(ScenarioFactionSlotKeys.Infiltrator),
            option => option.IsRequired);
        Assert.Contains(promisedWorld.GetFactionOptions(ScenarioFactionSlotKeys.Invader),
            option => option.IsRequired);
        Assert.NotEmpty(rules.FactionPlanetPresence.Rules);
        Assert.Equal(
            new[] { 1, 2, 3, 4, 5 },
            rules.PlanetTemplateEligibility.GetEligibleTemplateIds(
                PlanetTemplateEligibilityKeys.PromisedWorld));
        Assert.Equal(
            new[] { 1, 3, 4, 5 },
            rules.PlanetTemplateEligibility.GetEligibleTemplateIds(
                PlanetTemplateEligibilityKeys.OrkGhostSource));
    }

    [Fact]
    public void RulesDatabase_MissingRequiredTable_FailsBeforeHydration()
    {
        InvalidOperationException exception = AssertRulesDatabaseRejects(
            "missing-table",
            "PRAGMA foreign_keys = OFF; DROP TABLE PlanetTemplate;");

        Assert.True(
            exception.Message.Contains("required tables", StringComparison.OrdinalIgnoreCase),
            exception.Message);
        Assert.True(
            exception.Message.Contains("PlanetTemplate", StringComparison.OrdinalIgnoreCase),
            exception.Message);
    }

    [Fact]
    public void RulesDatabase_MissingPlanetTemplateEligibilityTable_FailsBeforeHydration()
    {
        InvalidOperationException exception = AssertRulesDatabaseRejects(
            "missing-planet-template-eligibility-table",
            "DROP TABLE PlanetTemplateEligibility;");

        Assert.Contains("required tables", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PlanetTemplateEligibility", exception.Message);
    }

    [Fact]
    public void RulesDatabase_RequiredCollectionsCannotBeEmpty()
    {
        InvalidOperationException exception = AssertRulesDatabaseRejects(
            "empty-planet-templates",
            "DELETE FROM PlanetTemplateEligibility; "
                + "PRAGMA foreign_keys = OFF; DELETE FROM PlanetTemplate;");

        Assert.True(
            exception.Message.Contains("PlanetTemplate", StringComparison.OrdinalIgnoreCase),
            exception.Message);
        Assert.True(
            exception.Message.Contains("empty", StringComparison.OrdinalIgnoreCase),
            exception.Message);
    }

    [Fact]
    public void RulesDatabase_PlanetTemplateProbabilitiesRequirePositiveTotal()
    {
        InvalidOperationException exception = AssertRulesDatabaseRejects(
            "zero-planet-probabilities",
            "UPDATE PlanetTemplate SET Probability = 0;");

        Assert.True(
            exception.Message.Contains("positive total", StringComparison.OrdinalIgnoreCase),
            exception.Message);
    }

    [Fact]
    public void RulesDatabase_PlanetTemplateEligibilityRequiresEachGenerationContext()
    {
        InvalidOperationException exception = AssertRulesDatabaseRejects(
            "missing-ork-ghost-context",
            "DELETE FROM PlanetTemplateEligibility "
                + "WHERE ContextKey = 'ambient.ork_ghost_source';");

        Assert.Contains(PlanetTemplateEligibilityKeys.OrkGhostSource, exception.Message);
        Assert.Contains("at least one template", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RulesDatabase_PlanetTemplateEligibilityRequiresPositiveContextProbability()
    {
        InvalidOperationException exception = AssertRulesDatabaseRejects(
            "zero-ork-ghost-probabilities",
            "UPDATE PlanetTemplate SET Probability = 0 "
                + "WHERE Id IN (SELECT PlanetTemplateId FROM PlanetTemplateEligibility "
                + "WHERE ContextKey = 'ambient.ork_ghost_source');");

        Assert.Contains(PlanetTemplateEligibilityKeys.OrkGhostSource, exception.Message);
        Assert.Contains("positive total", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlanetTemplateEligibility_UsesStableAssignmentsAfterTemplateRename()
    {
        GameRulesData original = new(RulesDatabaseFixture.DatabasePath);
        GameRulesData renamed = LoadRulesWithMutation(
            "renamed-planet-templates",
            "UPDATE PlanetTemplate SET Name = 'Localized world ' || Id;");

        Assert.Equal(
            original.PlanetTemplateEligibility.GetEligibleTemplateIds(
                PlanetTemplateEligibilityKeys.PromisedWorld),
            renamed.PlanetTemplateEligibility.GetEligibleTemplateIds(
                PlanetTemplateEligibilityKeys.PromisedWorld));
        Assert.Equal(
            original.PlanetTemplateEligibility.GetEligibleTemplateIds(
                PlanetTemplateEligibilityKeys.OrkGhostSource),
            renamed.PlanetTemplateEligibility.GetEligibleTemplateIds(
                PlanetTemplateEligibilityKeys.OrkGhostSource));
    }

    [Theory]
    [InlineData(
        "missing-player-boats",
        "DELETE FROM BoatTemplate WHERE FactionId = (SELECT Id FROM Faction WHERE IsPlayerFaction = 1);",
        "BoatTemplate",
        true)]
    [InlineData(
        "missing-player-fleets",
        "DELETE FROM FleetTemplateShipTemplate "
            + "WHERE FleetTemplateId IN (SELECT Id FROM FleetTemplate "
            + "WHERE FactionId = (SELECT Id FROM Faction WHERE IsPlayerFaction = 1)); "
            + "DELETE FROM FleetTemplate "
            + "WHERE FactionId = (SELECT Id FROM Faction WHERE IsPlayerFaction = 1);",
        "FleetTemplate",
        true)]
    [InlineData(
        "player-fleet-without-ships",
        "DELETE FROM FleetTemplateShipTemplate "
            + "WHERE FleetTemplateId IN (SELECT Id FROM FleetTemplate "
            + "WHERE FactionId = (SELECT Id FROM Faction WHERE IsPlayerFaction = 1));",
        "ShipTemplates",
        false)]
    [InlineData(
        "missing-player-ships",
        "PRAGMA foreign_keys = OFF; DELETE FROM ShipTemplate "
            + "WHERE FactionId = (SELECT Id FROM Faction WHERE IsPlayerFaction = 1);",
        "ShipTemplate",
        false)]
    public void RulesDatabase_PlayerFactionRequiresFleetCarrierPrerequisites(
        string suffix,
        string mutationSql,
        string expectedMessage,
        bool expectPlayerMessage)
    {
        InvalidOperationException exception = AssertRulesDatabaseRejects(suffix, mutationSql);

        Assert.True(
            exception.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase),
            exception.Message);
        Assert.Contains("Rules database", exception.Message, StringComparison.OrdinalIgnoreCase);
        if (expectPlayerMessage)
        {
            Assert.Contains("player faction", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void RulesDatabase_DanglingSkillTemplateReferenceReportsSourceRelation()
    {
        InvalidOperationException exception = AssertRulesDatabaseRejects(
            "missing-skill-reference",
            "PRAGMA foreign_keys = OFF; "
                + "UPDATE SkillTemplate SET BaseSkillId = -1;");

        Assert.True(
            exception.Message.Contains("SkillTemplate", StringComparison.OrdinalIgnoreCase),
            exception.Message);
        Assert.True(
            exception.Message.Contains("BaseSkillId", StringComparison.OrdinalIgnoreCase),
            exception.Message);
        Assert.True(
            exception.Message.Contains("missing id", StringComparison.OrdinalIgnoreCase),
            exception.Message);
    }

    [Fact]
    public void RulesDatabase_ExplicitlyOptionalCompatibilityTablesMayBeAbsent()
    {
        GameRulesData rules = LoadRulesWithMutation(
            "optional-extensions",
            "DROP TABLE SkillRoleAssignment; "
                + "DROP TABLE RatingConsumerAssignment; "
                + "DROP TABLE AwardFamily;");

        Assert.NotNull(rules);
        Assert.Same(
            rules.BaseSkillMap.Values.Single(skill => skill.SkillKey == SkillRoleKeys.Stealth),
            rules.Skills.Stealth);
        Assert.Equal(
            "core:honor_gun",
            rules.AwardCatalog.Get(AwardTypes.Gun).IconAssetKey);
    }

    [Fact]
    public void ScoutTrainingOptions_UseStableKeysAndAllowAddedOrRenamedRows()
    {
        GameRulesData rules = LoadRulesWithMutation(
            "scout-training-options",
            "UPDATE TrainingProfile "
                + "SET Name = 'Localized balanced training' "
                + "WHERE Id = (SELECT TrainingProfileId FROM ScoutTrainingOption "
                + "WHERE OptionKey = 'scout.balanced'); "
                + "INSERT INTO ScoutTrainingOption "
                + "(OptionKey, DisplayName, TrainingProfileId, SortOrder) "
                + "SELECT 'scout.custom', 'Custom', TrainingProfileId, -1 "
                + "FROM ScoutTrainingOption WHERE OptionKey = 'scout.physical';");

        Assert.Equal("Localized balanced training",
            rules.ScoutTrainingOptions.GetRequired(ScoutTrainingOptionKeys.Balanced)
                .Profile.Name);
        Assert.Equal(
            "scout.custom",
            rules.ScoutTrainingOptions.Options[0].Key);
        Assert.Equal("Custom",
            rules.ScoutTrainingOptions.GetRequired("scout.custom").DisplayName);
    }

    [Fact]
    public void ScoutTrainingOptions_RequireBalancedAsTheDefaultOption()
    {
        InvalidOperationException exception = AssertRulesDatabaseRejects(
            "missing-balanced-scout-option",
            "DELETE FROM ScoutTrainingOption WHERE OptionKey = 'scout.balanced';");

        Assert.Contains(ScoutTrainingOptionKeys.Balanced, exception.Message);
    }

    [Fact]
    public void RulesDatabase_OptionalEquipmentExtensionsMayBeAbsentWithoutDiscardingCoreCatalog()
    {
        GameRulesData rules = LoadRulesWithMutation(
            "optional-equipment-extensions",
            "DROP TABLE EquipmentGearProfile; DROP TABLE EquipmentRequirement;");

        Assert.NotEmpty(rules.EquipmentTemplates);
        Assert.NotEmpty(rules.EquipmentKits);
    }

    [Fact]
    public void ChapterDoctrine_ResolvesRenamedTemplatesThroughStableAssignments()
    {
        string temporaryDatabasePath = Path.Combine(
            Path.GetTempPath(), $"onlywar-chapter-doctrine-{Guid.NewGuid():N}.s3db");
        File.Copy(RulesDatabaseFixture.DatabasePath, temporaryDatabasePath);

        try
        {
            using (SqliteConnection connection = new(new SqliteConnectionStringBuilder
            {
                DataSource = temporaryDatabasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString()))
            {
                connection.Open();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
UPDATE SoldierTemplate
SET Name = 'Renamed soldier ' || Id
WHERE FactionId = (SELECT Id FROM Faction WHERE IsPlayerFaction = 1);
UPDATE SquadTemplate
SET Name = 'Renamed squad ' || Id
WHERE FactionId = (SELECT Id FROM Faction WHERE IsPlayerFaction = 1);
UPDATE UnitTemplate
SET Name = 'Renamed unit ' || Id
WHERE FactionId = (SELECT Id FROM Faction WHERE IsPlayerFaction = 1);";
                command.ExecuteNonQuery();
            }

            GameRulesData rules = new(temporaryDatabasePath);
            Assert.StartsWith("Renamed soldier ", rules.ChapterDoctrine.TacticalMarine.Name);
            Assert.StartsWith("Renamed squad ", rules.ChapterDoctrine.TacticalSquad.Name);
            Assert.StartsWith("Renamed unit ", rules.ChapterDoctrine.BattleCompany.Name);

            Assert.Equal(
                new[]
                {
                    rules.ChapterDoctrine.VeteranCompany,
                    rules.ChapterDoctrine.BattleCompany,
                    rules.ChapterDoctrine.BattleCompany,
                    rules.ChapterDoctrine.BattleCompany,
                    rules.ChapterDoctrine.BattleCompany,
                    rules.ChapterDoctrine.TacticalCompany,
                    rules.ChapterDoctrine.TacticalCompany,
                    rules.ChapterDoctrine.AssaultCompany,
                    rules.ChapterDoctrine.DevastatorCompany,
                    rules.ChapterDoctrine.ScoutCompany
                },
                rules.ChapterDoctrine.GetOrderedChildUnits(rules.ChapterDoctrine.RootUnit));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(temporaryDatabasePath))
            {
                File.Delete(temporaryDatabasePath);
            }
        }
    }

    [Fact]
    public void RulesDatabase_DoesNotContainCodeOwnedSupplyTables()
    {
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = RulesDatabaseFixture.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name LIKE 'Supply%'";

        Assert.Equal(0L, (long)command.ExecuteScalar());
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

    private static InvalidOperationException AssertRulesDatabaseRejects(
        string suffix,
        string mutationSql)
    {
        string databasePath = CreateMutatedRulesDatabase(suffix, mutationSql);
        try
        {
            return Assert.Throws<InvalidOperationException>(() => new GameRulesData(databasePath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private static GameRulesData LoadRulesWithMutation(string suffix, string mutationSql)
    {
        string databasePath = CreateMutatedRulesDatabase(suffix, mutationSql);
        try
        {
            return new GameRulesData(databasePath);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private static string CreateMutatedRulesDatabase(string suffix, string mutationSql)
    {
        string databasePath = Path.Combine(
            Path.GetTempPath(), $"onlywar-rules-validation-{suffix}-{Guid.NewGuid():N}.s3db");
        File.Copy(RulesDatabaseFixture.DatabasePath, databasePath);

        using (SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString()))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = mutationSql;
            command.ExecuteNonQuery();
        }

        return databasePath;
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
            Assert.Equal(0, flamer.Accuracy);
        }

        var bolter = rules.RangedWeaponTemplates[1];
        Assert.False(bolter.IsTemplateWeapon);
        Assert.Equal(0, bolter.AreaRadius);
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
    public void EquipmentCatalog_ModelsFiniteGrenadesAndBiologicalWeapons()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        EquipmentRulesCatalog catalog = rules.EquipmentCatalog;
        EquipmentTemplate grenade = catalog.EquipmentTemplates[
            EquipmentRulesCatalog.GetRangedEquipmentId(35)];
        EquipmentTemplate bioWeapon = catalog.EquipmentTemplates[
            EquipmentRulesCatalog.GetRangedEquipmentId(13)];

        Assert.Equal(AmmunitionBehavior.ConsumableItem,
            grenade.RangedProfile.AmmunitionBehavior);
        Assert.Equal(AmmunitionBehavior.SelfRegenerating,
            bioWeapon.RangedProfile.AmmunitionBehavior);
        Assert.Null(grenade.RangedProfile.AmmunitionType);
        Assert.NotNull(catalog.EquipmentTemplates[
            EquipmentRulesCatalog.GetAmmunitionPackageId(
                EquipmentRulesCatalog.GetAmmunitionTypeId(1))]);
        Assert.All(catalog.EquipmentKits.Values, kit =>
        {
            EquipmentValidationResult validation = EquipmentLoadoutValidator.Validate(kit);
            Assert.True(validation.IsValid,
                $"{kit.Name}: {string.Join("; ", validation.Issues.Select(issue => issue.Message))}");
        });
    }

    [Fact]
    public void OrkMegaArmor_IsLoadedAsNonRunningArmor_InBothEquipmentPipelines()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        Faction orks = rules.Factions.Single(faction => faction.Name == "Orks");
        ArmorTemplate megaArmor = orks.SquadTemplates[31].Armor;
        EquipmentTemplate megaEquipment = rules.EquipmentCatalog.EquipmentTemplates[
            EquipmentRulesCatalog.GetArmorEquipmentId(megaArmor.Id)];

        Assert.Equal("Mega Armor", megaArmor.Name);
        Assert.Equal(25, megaArmor.ArmorProvided);
        Assert.Equal(-4, megaArmor.StealthModifier);
        Assert.True(megaArmor.PreventsRunning);
        Assert.Equal(25, megaEquipment.ArmorProfile.ArmorProvided);
        Assert.Equal(-4, megaEquipment.ArmorProfile.StealthModifier);
        Assert.True(megaEquipment.ArmorProfile.PreventsRunning);
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
