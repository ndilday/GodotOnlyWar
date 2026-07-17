using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Generation;

/// <summary>
/// Phase 2 of Design/Active/MoraleAndRout.md §9: GenerateGenericForce purchases
/// synapse-providing squads on a BV ratio of coverage-needing squads, with a
/// minimum-force floor. Uses seeded RNG per the shared-RNG test convention (see
/// OnlyWar.Tests.TestCollections.SharedState).
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class ForceGeneratorSynapseTests
{
    // Ego 8 — a Termagaunt-analog: cheap, no synapse, fails the rear-guard Ego gate
    // (MoraleConstants.RearGuardEgoThreshold = 12), so it "needs coverage" (§9).
    private static readonly Species GauntLikeSpecies = CreateSpecies(101, "Test Gaunt", ego: 8);

    // Ego 20, no synapse — a Genestealer-analog: independent-willed, passes the Ego gate,
    // so it never "needs coverage" and never triggers a synapse purchase (§9: "Genestealers
    // (Ego 20) therefore buy no Warriors").
    private static readonly Species IndependentWilledSpecies = CreateSpecies(102, "Test Genestealer", ego: 20);

    // Ego 20 + Synapse — a Tyranid-Warrior-analog: cheap-ish synapse provider.
    private static readonly Species WarriorLikeSpecies =
        CreateSpecies(103, "Test Warrior", ego: 20, abilities: SpeciesAbilities.Synapse);

    // Ego 30 + Synapse — a Hive-Tyrant/Patriarch-analog: expensive HQ synapse provider.
    private static readonly Species HqSynapseSpecies =
        CreateSpecies(104, "Test Synapse HQ", ego: 30, abilities: SpeciesAbilities.Synapse);

    [Theory]
    [InlineData(60)]
    [InlineData(300)]
    [InlineData(1500)]
    public void TyranidLikeForce_AlwaysFieldsAtLeastOneSynapseSquadAcrossBudgetSweep(long budget)
    {
        RNG.Reset(1);
        SquadTemplate hiveTyrant = CreateTemplate(1, "Hive Tyrant", SquadTypes.HQ, HqSynapseSpecies, 1, 84);
        SquadTemplate warriors = CreateTemplate(2, "Warriors", SquadTypes.Elite, WarriorLikeSpecies, 3, 33);
        SquadTemplate gaunts = CreateTemplate(3, "Gaunts", SquadTypes.None, GauntLikeSpecies, 10, 60);
        Faction faction = CreateFaction(hiveTyrant, warriors, gaunts);

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = budget,
            Profile = ForceCompositionProfile.AssaultForce
        });

        Assert.NotEmpty(generated);
        Assert.Contains(generated, s => s.SquadTemplate.ProvidesSynapse);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(300)]
    [InlineData(1500)]
    public void CultLikeForce_AlwaysFieldsAtLeastOneSynapseSquadAcrossBudgetSweep(long budget)
    {
        RNG.Reset(1);
        // Mirrors the real rules DB shape (Design §9): both synapse providers for this
        // faction (Patriarch, Primus) are HQ-typed squad templates, not a cheap non-HQ
        // source like the Tyranid Warrior Squad.
        SquadTemplate patriarch = CreateTemplate(1, "Patriarch", SquadTypes.HQ, HqSynapseSpecies, 1, 39);
        SquadTemplate primus = CreateTemplate(2, "Primus", SquadTypes.HQ, WarriorLikeSpecies, 1, 21);
        SquadTemplate broodBrothers = CreateTemplate(3, "Brood Brothers", SquadTypes.None, GauntLikeSpecies, 2, 14);
        Faction faction = CreateFaction(patriarch, primus, broodBrothers);

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = budget,
            Profile = ForceCompositionProfile.AssaultForce
        });

        Assert.NotEmpty(generated);
        Assert.Contains(generated, s => s.SquadTemplate.ProvidesSynapse);
    }

    [Fact]
    public void SynapseSquadCount_ScalesUpWithCoverageNeedingBudget()
    {
        // Larger swarms field more Warriors (§9, reason 1). Holding the same template set
        // fixed and growing the budget should never field *fewer* synapse squads.
        SquadTemplate warriors = CreateTemplate(1, "Warriors", SquadTypes.Elite, WarriorLikeSpecies, 3, 33);
        SquadTemplate gaunts = CreateTemplate(2, "Gaunts", SquadTypes.None, GauntLikeSpecies, 10, 60);
        Faction faction = CreateFaction(warriors, gaunts);

        RNG.Reset(1);
        int smallCount = SynapseSquadCount(GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 300,
            Profile = ForceCompositionProfile.AssaultForce
        }));

        RNG.Reset(1);
        int largeCount = SynapseSquadCount(GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 3000,
            Profile = ForceCompositionProfile.AssaultForce
        }));

        Assert.True(
            largeCount >= smallCount,
            $"expected synapse squad count to scale with budget, got small={smallCount}, large={largeCount}");
        Assert.True(largeCount > 1, $"expected more than one synapse squad at 10x budget, got {largeCount}");
    }

    [Fact]
    public void RatioPurchase_KeepsPaceWithCoverageNeedingBvBought()
    {
        // §9's illustrative ratio: roughly one Warrior squad (~33 BV) per ~150 BV of gaunts.
        // Assert the realized force never falls behind that ratio once the budget has
        // comfortable headroom to afford it (the ratio is statistical, not a hard
        // per-BV guarantee under a tight budget — see the floor test below for the
        // small-budget case instead).
        RNG.Reset(7);
        SquadTemplate warriors = CreateTemplate(1, "Warriors", SquadTypes.Elite, WarriorLikeSpecies, 3, 33);
        SquadTemplate gaunts = CreateTemplate(2, "Gaunts", SquadTypes.None, GauntLikeSpecies, 10, 60);
        Faction faction = CreateFaction(warriors, gaunts);

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 4500,
            Profile = ForceCompositionProfile.AssaultForce
        });

        long coverageNeedingBv = generated
            .Where(s => !s.SquadTemplate.ProvidesSynapse && s.SquadTemplate.SquadEgo < MoraleConstants.RearGuardEgoThreshold)
            .Sum(s => (long)s.SquadTemplate.BattleValue);
        int synapseSquads = SynapseSquadCount(generated);
        int minimumOwed = (int)(coverageNeedingBv / MoraleConstants.SynapseRatioBV);

        Assert.True(
            synapseSquads >= minimumOwed,
            $"coverage-needing BV {coverageNeedingBv} owes >= {minimumOwed} synapse squads, got {synapseSquads}");
    }

    [Fact]
    public void MinimumForceFloor_AddsSynapseSquadWhenBudgetTooSmallForRatioPurchase()
    {
        // The cheapest synapse template costs more than the whole budget, so the
        // interleaved ratio purchase inside the while loop can never afford one. The floor
        // still adds one after the loop, exceeding the nominal budget (§9, accepted quirk).
        RNG.Reset(1);
        SquadTemplate warriors = CreateTemplate(1, "Warriors", SquadTypes.Elite, WarriorLikeSpecies, 3, 900);
        SquadTemplate gaunts = CreateTemplate(2, "Gaunts", SquadTypes.None, GauntLikeSpecies, 5, 20);
        Faction faction = CreateFaction(warriors, gaunts);

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 20,
            Profile = ForceCompositionProfile.AssaultForce
        });

        Assert.Contains(generated, s => s.SquadTemplate.ProvidesSynapse);
        long totalBv = generated.Sum(s => (long)s.SquadTemplate.BattleValue);
        Assert.True(totalBv > 20, $"expected the floor to push the force above budget, got {totalBv}");
    }

    [Fact]
    public void ZeroCoverageNeedingBudget_BuysNoExtraSynapseSquad()
    {
        // A faction whose only affordable non-HQ template is independent-willed (passes
        // the Ego gate on its own) never accumulates coverage-needing BV, so it should not
        // be floored into buying a synapse escort it does not need — even though the
        // faction has a synapse template on its roster (so factionHasSynapse is true).
        RNG.Reset(1);
        SquadTemplate synapseHq = CreateTemplate(1, "Synapse HQ", SquadTypes.HQ, HqSynapseSpecies, 1, 5000);
        SquadTemplate independentWilled = CreateTemplate(
            2, "Genestealers", SquadTypes.None, IndependentWilledSpecies, 3, 60);
        Faction faction = CreateFaction(synapseHq, independentWilled);

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 300,
            Profile = ForceCompositionProfile.AssaultForce
        });

        Assert.NotEmpty(generated);
        Assert.DoesNotContain(generated, s => s.SquadTemplate.ProvidesSynapse);
    }

    [Fact]
    public void FactionWithoutAnySynapseTemplate_GenerationIsUnaffected()
    {
        // Non-synapse factions (Space Marines, PDF, Orks) must see no behavior change.
        RNG.Reset(1);
        SquadTemplate line = CreateTemplate(1, "Line", SquadTypes.None, GauntLikeSpecies, 5, 40);
        Faction faction = CreateFaction(line);

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 400,
            Profile = ForceCompositionProfile.AssaultForce
        });

        Assert.NotEmpty(generated);
        Assert.All(generated, s => Assert.False(s.SquadTemplate.ProvidesSynapse));
        Assert.Equal(400, generated.Sum(s => (long)s.SquadTemplate.BattleValue));
    }

    private static int SynapseSquadCount(IEnumerable<Squad> squads) =>
        squads.Count(s => s.SquadTemplate.ProvidesSynapse);

    private static Species CreateSpecies(int id, string name, float ego, SpeciesAbilities abilities = SpeciesAbilities.None)
    {
        return new Species(
            id, name,
            Value(10), Value(10), Value(10), Value(10), Value(10), Value(ego), Value(10),
            Value(0), Value(10), Value(6), Value(1),
            1, 1, 0f, 0f,
            abilities,
            OnlyWar.Models.Soldiers.HumanBodyTemplate.Instance,
            TestModelFactory.DefaultUnarmedWeapon,
            synapseRadius: abilities.HasFlag(SpeciesAbilities.Synapse) ? 1000f : 0f);
    }

    private static NormalizedValueTemplate Value(float value) => new()
    {
        BaseValue = value,
        StandardDeviation = 0
    };

    private static SquadTemplate CreateTemplate(
        int id,
        string name,
        SquadTypes squadTypes,
        Species species,
        byte maxSoldiers,
        int battleValue,
        byte minSoldiers = 0)
    {
        SoldierTemplate trooper = new(
            id, species, name + " Trooper",
            1, 1, false, 0, [], null, battleValue / maxSoldiers);
        return new SquadTemplate(
            id,
            name,
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(trooper, minSoldiers, maxSoldiers)],
            squadTypes);
    }

    private static List<Squad> GenerateForce(ForceGenerationRequest request) =>
        ForceGenerator.GenerateForce(request, StaticRNG.Instance);

    private static Faction CreateFaction(params SquadTemplate[] squadTemplates)
    {
        return new Faction(
            1,
            "Test Faction",
            Color.Black,
            false,
            false,
            false,
            GrowthType.None,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate> { [TestModelFactory.MarineTemplate.Id] = TestModelFactory.MarineTemplate },
            squadTemplates.ToDictionary(st => st.Id),
            new Dictionary<int, UnitTemplate>(),
            null,
            null,
            null);
    }
}
