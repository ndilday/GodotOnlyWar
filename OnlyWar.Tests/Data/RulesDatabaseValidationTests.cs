using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
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
        Assert.NotNull(rules.SupplyEconomyRules);
        Assert.NotEmpty(rules.SupplyEconomyRules.RequestValuation.ThroughputBands);
        Assert.True(rules.SupplyEconomyRules.RequestValuation.RequisitionPerBattleValueTime > 0);
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
