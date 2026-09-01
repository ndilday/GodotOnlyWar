using System;
using System.Collections.Generic;
using OnlyWar.Helpers.Database.GameRules;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using Microsoft.Data.Sqlite;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class RatingConsumerBindingsTests
{
    [Fact]
    public void ConsumerRoles_ReadDataOwnedRatingKeys_WithoutUsingShippedNames()
    {
        RatingConsumerBindings bindings = new(
            [
                new(RatingConsumerRoleKeys.MeleeCombat, "blade_mastery"),
                new(RatingConsumerRoleKeys.RangedCombat, "fire_control"),
                new(RatingConsumerRoleKeys.CommandLeadership, "presence_of_command"),
                new(RatingConsumerRoleKeys.MedicalCapacity, "chirurgical_lore"),
                new(RatingConsumerRoleKeys.TechnicalCapability, "machine_lore"),
                new(RatingConsumerRoleKeys.SpiritualCapability, "litany"),
                new(RatingConsumerRoleKeys.AncientService, "gene_memory")
            ]);
        SoldierEvaluation evaluation = new(
            new Date(41, 1000, 1),
            new Dictionary<string, float>
            {
                ["blade_mastery"] = 91,
                ["fire_control"] = 117,
                ["presence_of_command"] = 63,
                ["chirurgical_lore"] = 102,
                ["machine_lore"] = 84,
                ["litany"] = 96,
                ["gene_memory"] = 110
            });

        Assert.Equal(117, bindings.Get(evaluation, RatingConsumerRole.RangedCombat));
        Assert.Equal(102, bindings.Get(evaluation, RatingConsumerRole.MedicalCapacity));
        Assert.Equal("presence_of_command",
            bindings.GetRatingKey(RatingConsumerRole.CommandLeadership));
    }

    [Fact]
    public void ConsumerBindings_RejectDuplicateRoles()
    {
        Assert.Throws<InvalidOperationException>(() => new RatingConsumerBindings(
            [
                new(RatingConsumerRoleKeys.MeleeCombat, "blade_mastery"),
                new(RatingConsumerRoleKeys.MeleeCombat, "close_combat")
            ]));
    }

    [Fact]
    public void AwardFamilies_CarryModOwnedIconKeysAndOrdering()
    {
        AwardFamilyCatalog catalog = new(
            [
                new AwardFamilyDefinition(
                    "iron_halo:duelist",
                    "Duelist's Halo",
                    "iron_halo:icon_duelist",
                    42,
                    "combat",
                    "duelist")
            ]);

        AwardFamilyDefinition family = catalog.Get("iron_halo:duelist");

        Assert.Equal("iron_halo:icon_duelist", family.IconAssetKey);
        Assert.Equal(42, family.SortOrder);
        Assert.Equal("combat", family.SummaryGroup);
    }

    [Fact]
    public void RulesDataAccess_LoadsExplicitConsumerAndAwardPresentationTables()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE RatingConsumerAssignment (RoleKey TEXT, RatingKey TEXT);
                INSERT INTO RatingConsumerAssignment VALUES ('ranged_combat', 'fire_control');
                CREATE TABLE AwardFamily (
                    AwardFamilyKey TEXT,
                    DisplayName TEXT,
                    IconAssetKey TEXT,
                    SortOrder INTEGER,
                    SummaryGroup TEXT,
                    StackingGroup TEXT
                );
                INSERT INTO AwardFamily VALUES
                    ('iron_halo:duelist', 'Duelist''s Halo', 'iron_halo:icon_duelist', 42, 'combat', 'duelist');
                """;
            command.ExecuteNonQuery();
        }

        RatingDataAccess access = new();
        Assert.Equal("fire_control",
            Assert.Single(access.GetRatingConsumerAssignments(connection)).RatingKey);
        Assert.Equal("iron_halo:icon_duelist",
            Assert.Single(access.GetAwardFamilies(connection)).IconAssetKey);
    }
}
