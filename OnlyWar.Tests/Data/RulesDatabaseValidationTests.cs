using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Generation.World;
using OnlyWar.Domain;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Tests.Fixtures;
using Microsoft.Data.Sqlite;
using Xunit;

namespace OnlyWar.Tests.Data;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class RulesDatabaseValidationTests
{
    [Fact]
    public void HydratedCatalogRetainsTemplateIdentityAndIsIndependentOfItsSourceFile()
    {
        string path = RulesDatabaseFixture.CreateTemporaryCopy("onlywar-catalog");
        GameRulesBlob values;
        try
        {
            values = new OnlyWar.Persistence.Database.GameRules.GameRulesDataAccess().GetData(path);
        }
        finally
        {
            RulesDatabaseFixture.DeleteTemporaryCopy(path);
        }

        GameRulesData catalog = new(values);

        Assert.Same(values.Factions.Single(faction => faction.IsPlayerFaction), catalog.PlayerFaction);
        Assert.Same(values.BaseSkills, catalog.BaseSkillMap);
        Assert.Same(values.EquipmentCatalog, catalog.EquipmentCatalog);
        Assert.NotEmpty(catalog.ChapterDoctrines);
        Assert.NotNull(catalog.Skills);
        Assert.False(File.Exists(path));
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
    public void RulesDatabase_MissingSectorGenerationProfileTable_FailsBeforeHydration()
    {
        InvalidOperationException exception = AssertRulesDatabaseRejects(
            "missing-sector-generation-profile-table",
            "DROP TABLE SectorGenerationProfile;");

        Assert.Contains("required tables", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SectorGenerationProfile", exception.Message);
    }

    [Fact]
    public void RulesDatabase_SectorGenerationProfileCannotBeEmpty()
    {
        InvalidOperationException exception = AssertRulesDatabaseRejects(
            "empty-sector-generation-profile",
            "DELETE FROM SectorGenerationProfile;");

        Assert.Contains("SectorGenerationProfile", exception.Message);
        Assert.Contains("empty", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        "invalid-sector-generation-probability",
        "PRAGMA ignore_check_constraints = ON; "
            + "UPDATE SectorGenerationProfile SET PlanetSpawnProbability = 1.01 "
            + "WHERE IsDefault = 1;",
        "planet spawn probability")]
    [InlineData(
        "invalid-sector-generation-dimensions",
        "PRAGMA ignore_check_constraints = ON; "
            + "UPDATE SectorGenerationProfile SET SectorWidth = 0 "
            + "WHERE IsDefault = 1;",
        "sectorWidth")]
    public void RulesDatabase_InvalidSectorGenerationProfileValuesFailValidation(
        string suffix,
        string mutationSql,
        string expectedMessage)
    {
        InvalidOperationException exception = AssertRulesDatabaseRejects(suffix, mutationSql);

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RulesDatabase_RequiresExactlyOneDefaultSectorGenerationProfile()
    {
        InvalidOperationException exception = AssertRulesDatabaseRejects(
            "multiple-default-sector-generation-profiles",
            "INSERT INTO SectorGenerationProfile "
                + "(ProfileKey, SectorWidth, SectorHeight, PlanetSpawnProbability, "
                + "MaxSubsectorDiameter, IsDefault) "
                + "SELECT 'alternate', SectorWidth, SectorHeight, PlanetSpawnProbability, "
                + "MaxSubsectorDiameter, 1 FROM SectorGenerationProfile "
                + "WHERE IsDefault = 1;");

        Assert.Contains("exactly one default", exception.Message, StringComparison.OrdinalIgnoreCase);
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
            "missing-ghost-population-context",
            "DELETE FROM PlanetTemplateEligibility "
                + "WHERE ContextKey = 'ambient.ghost_population_source';");

        Assert.Contains(PlanetTemplateEligibilityKeys.GhostPopulationSource, exception.Message);
        Assert.Contains("at least one template", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RulesDatabase_PlanetTemplateEligibilityRequiresPositiveContextProbability()
    {
        InvalidOperationException exception = AssertRulesDatabaseRejects(
            "zero-ghost-population-probabilities",
            "UPDATE PlanetTemplate SET Probability = 0 "
                + "WHERE Id IN (SELECT PlanetTemplateId FROM PlanetTemplateEligibility "
                + "WHERE ContextKey = 'ambient.ghost_population_source');");

        Assert.Contains(PlanetTemplateEligibilityKeys.GhostPopulationSource, exception.Message);
        Assert.Contains("positive total", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlanetTemplateEligibility_UsesStableAssignmentsAfterTemplateRename()
    {
        GameRulesData original = OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(RulesDatabaseFixture.DatabasePath);
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
                PlanetTemplateEligibilityKeys.GhostPopulationSource),
            renamed.PlanetTemplateEligibility.GetEligibleTemplateIds(
                PlanetTemplateEligibilityKeys.GhostPopulationSource));
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
    public void RangedWeaponTemplates_MagazineWeaponsCarryAnAuthoredCaliber()
    {
        GameRulesBlob rules = RulesDatabaseFixture.LoadRules();

        Assert.All(
            rules.RangedWeaponTemplates.Values.Where(template =>
                template.AmmunitionBehavior == AmmunitionBehavior.Magazine),
            template => Assert.True(
                template.AmmunitionType != null,
                $"{template.Id} {template.Name} has no ammunition type."));

        // Calibers, not one type per weapon: every boltgun and bolt pistol template fires the
        // same rounds, and the Ork guns that shipped untyped now have counted ammunition.
        AmmunitionType bolt = rules.RangedWeaponTemplates.Values
            .Single(template => template.Id == 0)
            .AmmunitionType;
        Assert.All(
            rules.RangedWeaponTemplates.Values.Where(template =>
                template.Name is "Boltgun" or "Bolt Pistol"),
            template => Assert.Equal(bolt, template.AmmunitionType));
        Assert.NotNull(rules.RangedWeaponTemplates.Values
            .Single(template => template.Name == "Big Shoota")
            .AmmunitionType);
    }

    [Fact]
    public void EquipmentKits_CarryStandardSpareMagazinesForEveryCaliber()
    {
        GameRulesBlob rules = RulesDatabaseFixture.LoadRules();

        Assert.NotEmpty(rules.EquipmentCatalog.EquipmentKits);
        foreach (EquipmentKitTemplate kit in rules.EquipmentCatalog.EquipmentKits.Values)
        {
            // Per caliber, because weapons sharing one also share its reserve.
            IEnumerable<IGrouping<AmmunitionType, EquipmentKitEntry>> weaponsByCaliber = kit.Items
                .Where(item => item.Equipment.RangedProfile?.AmmunitionType != null
                    && item.Equipment.RangedProfile.AmmunitionBehavior
                        is AmmunitionBehavior.Magazine or AmmunitionBehavior.Incremental)
                .GroupBy(item => item.Equipment.RangedProfile.AmmunitionType);
            foreach (IGrouping<AmmunitionType, EquipmentKitEntry> caliber in weaponsByCaliber)
            {
                int magazineRounds = caliber.Sum(item =>
                    item.Equipment.RangedProfile.LoadedCapacity * item.Quantity);
                int packageRounds = kit.Items
                    .Where(item => Equals(item.Equipment.AmmunitionProfile?.AmmunitionType, caliber.Key))
                    .Sum(item => item.Equipment.AmmunitionProfile.RoundsPerPackage * item.Quantity);
                Assert.True(
                    packageRounds >= EquipmentRulesCatalog.StandardSpareMagazines * magazineRounds,
                    $"Kit {kit.Id} {kit.Name} carries {packageRounds} spare {caliber.Key.Name} "
                    + $"for {magazineRounds} loaded.");
            }
        }
    }

    [Fact]
    public void RulesDatabase_MagazineWeaponWithoutAmmunitionTypeFailsValidation()
    {
        InvalidOperationException exception = AssertRulesDatabaseRejects(
            "untyped-magazine-weapon",
            "UPDATE RangedWeaponTemplate SET AmmunitionTypeId = NULL WHERE Name = 'Big Shoota';");

        Assert.Contains("Big Shoota", exception.Message);
        Assert.Contains("AmmunitionTypeId", exception.Message);
    }

    [Fact]
    public void ChapterDoctrine_ResolvesRenamedTemplatesThroughStableAssignments()
    {
        string temporaryDatabasePath = RulesDatabaseFixture.CreateTemporaryCopy("onlywar-chapter-doctrine");

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

            GameRulesData rules = OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(temporaryDatabasePath);
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
            RulesDatabaseFixture.DeleteTemporaryCopy(temporaryDatabasePath);
        }
    }

    private static InvalidOperationException AssertRulesDatabaseRejects(
        string suffix,
        string mutationSql)
    {
        string databasePath = CreateMutatedRulesDatabase(suffix, mutationSql);
        try
        {
            return Assert.Throws<InvalidOperationException>(() => OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(databasePath));
        }
        finally
        {
            RulesDatabaseFixture.DeleteTemporaryCopy(databasePath);
        }
    }

    private static GameRulesData LoadRulesWithMutation(string suffix, string mutationSql)
    {
        string databasePath = CreateMutatedRulesDatabase(suffix, mutationSql);
        try
        {
            return OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(databasePath);
        }
        finally
        {
            RulesDatabaseFixture.DeleteTemporaryCopy(databasePath);
        }
    }

    private static string CreateMutatedRulesDatabase(string suffix, string mutationSql)
    {
        string databasePath = RulesDatabaseFixture.CreateTemporaryCopy($"onlywar-rules-validation-{suffix}");

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
        }, new StaticRNG());

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
