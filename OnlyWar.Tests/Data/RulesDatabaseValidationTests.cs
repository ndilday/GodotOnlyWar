using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Models;
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
        Assert.NotNull(rules.PlayerFaction);
        Assert.NotNull(rules.DefaultFaction);
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
            Assert.Equal(10, flamer.FuelPerBurst);
            Assert.Equal(0, flamer.Accuracy);
        }

        var bolter = rules.RangedWeaponTemplates[1];
        Assert.False(bolter.IsTemplateWeapon);
        Assert.Equal(0, bolter.AreaRadius);
        Assert.Equal(0, bolter.FuelPerBurst);
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
        });

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
