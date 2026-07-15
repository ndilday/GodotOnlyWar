using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Database.GameRules;
using OnlyWar.Models.Equippables;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class SpeciesDefaultUnarmedWeaponTests
{
    [Fact]
    public void ShippedSpecies_ResolveDefaultUnarmedWeaponFromTheirOwnRulesData()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        var species = rules.Factions
            .SelectMany(faction => faction.Species?.Values ?? [])
            .ToDictionary(item => item.Name);

        Assert.Equal(12, species["Space Marine"].DefaultUnarmedWeaponTemplateId);
        Assert.Equal("Fist", species["Space Marine"].DefaultUnarmedWeapon.RelatedSkill.Name);

        // Human species are not restricted from sharing either weapon profile. The
        // shipped PDF mapping remains on Generic Melee to preserve its current training balance.
        Assert.Equal(15, species["PDF Trooper"].DefaultUnarmedWeaponTemplateId);
        Assert.Equal("Generic Melee", species["PDF Trooper"].DefaultUnarmedWeapon.RelatedSkill.Name);

        Assert.All(
            species.Values.Where(item => item.Name != "Space Marine"),
            item => Assert.Equal(15, item.DefaultUnarmedWeaponTemplateId));

        Assert.All(species.Values, item =>
        {
            Assert.Same(
                rules.MeleeWeaponTemplates[item.DefaultUnarmedWeaponTemplateId],
                item.DefaultUnarmedWeapon);
            Assert.Equal("Fist", item.DefaultUnarmedWeapon.Name);
        });
    }

    [Fact]
    public void Resolver_AllowsOrdinaryHumanSpeciesToSelectEitherUnarmedProfile()
    {
        var rules = RulesDatabaseFixture.LoadRules();

        MeleeWeaponTemplate weapon = SquadTemplateDataAccess.ResolveDefaultUnarmedWeapon(
            23, "PDF Trooper", 12, rules.MeleeWeaponTemplates);

        Assert.Equal("Fist", weapon.Name);
        Assert.Equal("Fist", weapon.RelatedSkill.Name);
    }

    [Fact]
    public void Resolver_FailsFast_WhenSpeciesReferencesUnknownWeaponTemplate()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        IReadOnlyDictionary<int, MeleeWeaponTemplate> templates = rules.MeleeWeaponTemplates;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            SquadTemplateDataAccess.ResolveDefaultUnarmedWeapon(
                23, "PDF Trooper", int.MaxValue, templates));

        Assert.Contains("PDF Trooper", exception.Message);
        Assert.Contains(int.MaxValue.ToString(), exception.Message);
    }
}
