using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Generation;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class ForceGeneratorTests
{
    [Fact]
    public void GenericForce_DoesNotAddHqBelowCommandScaleAndFillsBudget()
    {
        SquadTemplate hq = CreateTemplate(1, "HQ", SquadTypes.HQ, 1, 5);
        SquadTemplate heavy = CreateTemplate(2, "Heavy", SquadTypes.None, 1, 7);
        SquadTemplate line = CreateTemplate(3, "Line", SquadTypes.None, 1, 3);
        Faction faction = CreateFaction(hq, heavy, line);

        List<Squad> generated = ForceGenerator.GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 10,
            Profile = ForceCompositionProfile.Garrison
        });

        Assert.Equal(["Heavy", "Line"], generated.Select(s => s.SquadTemplate.Name).OrderBy(name => name).ToArray());
        Assert.Equal(10, SquadBattleValue(generated));
    }

    [Fact]
    public void GenericForce_AddsHqWhenBudgetCanAffordHqAndThreeNonHqSquads()
    {
        RNG.Reset(1);
        SquadTemplate hq = CreateTemplate(1, "HQ", SquadTypes.HQ, 1, 5);
        SquadTemplate line = CreateTemplate(2, "Line", SquadTypes.None, 1, 3);
        Faction faction = CreateFaction(hq, line);

        List<Squad> generated = ForceGenerator.GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 14,
            Profile = ForceCompositionProfile.Garrison
        });

        Assert.Equal("HQ", generated[0].SquadTemplate.Name);
        Assert.Equal(3, generated.Count(s => (s.SquadTemplate.SquadType & SquadTypes.HQ) == 0));
        Assert.Equal(14, SquadBattleValue(generated));
    }

    [Fact]
    public void GenericForce_DoesNotAddHqWhenBudgetCannotAffordThreeNonHqSquadsAfterward()
    {
        RNG.Reset(1);
        SquadTemplate hq = CreateTemplate(1, "HQ", SquadTypes.HQ, 1, 5);
        SquadTemplate line = CreateTemplate(2, "Line", SquadTypes.None, 1, 3);
        Faction faction = CreateFaction(hq, line);

        List<Squad> generated = ForceGenerator.GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 13,
            Profile = ForceCompositionProfile.Garrison
        });

        Assert.DoesNotContain(generated, squad => (squad.SquadTemplate.SquadType & SquadTypes.HQ) == SquadTypes.HQ);
        Assert.Equal(12, SquadBattleValue(generated));
    }

    [Fact]
    public void GenericForce_MixesAffordableTemplatesBeforeRepeating()
    {
        RNG.Reset(1);
        SquadTemplate heavy = CreateTemplate(1, "Heavy", SquadTypes.None, 1, 1000);
        SquadTemplate line = CreateTemplate(2, "Line", SquadTypes.None, 1, 500);
        Faction faction = CreateFaction(heavy, line);

        List<Squad> generated = ForceGenerator.GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 3000,
            Profile = ForceCompositionProfile.Garrison
        });

        string[] generatedNames = generated.Select(s => s.SquadTemplate.Name).ToArray();
        Assert.Contains("Heavy", generatedNames);
        Assert.Contains("Line", generatedNames);
        Assert.Equal(3000, SquadBattleValue(generated));
    }

    [Fact]
    public void GenericForce_ReturnsEmptyWhenNoNonHqTemplateFitsBudget()
    {
        SquadTemplate hq = CreateTemplate(1, "HQ", SquadTypes.HQ, 1, 1);
        SquadTemplate expensive = CreateTemplate(2, "Expensive", SquadTypes.None, 1, 10);
        Faction faction = CreateFaction(hq, expensive);

        List<Squad> generated = ForceGenerator.GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 5,
            Profile = ForceCompositionProfile.AssaultForce
        });

        Assert.Empty(generated);
    }

    [Fact]
    public void GenericForce_GeneratesPartialSquadWhenNoFullTemplateFitsBudget()
    {
        SquadTemplate line = CreateTemplate(1, "Line", SquadTypes.None, 5, 10, minSoldiers: 2);
        Faction faction = CreateFaction(line);

        List<Squad> generated = ForceGenerator.GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 6,
            Profile = ForceCompositionProfile.AssaultForce
        });

        Squad squad = Assert.Single(generated);
        Assert.Equal("Line", squad.SquadTemplate.Name);
        Assert.Equal(3, squad.Members.Count);
        Assert.Equal(6, SquadBattleValue(generated));
    }

    [Fact]
    public void GenericForce_FillsRemainderWithPartialCheapestSquad()
    {
        SquadTemplate heavy = CreateTemplate(1, "Heavy", SquadTypes.None, 1, 7);
        SquadTemplate line = CreateTemplate(2, "Line", SquadTypes.None, 10, 10, minSoldiers: 2);
        Faction faction = CreateFaction(heavy, line);

        List<Squad> generated = ForceGenerator.GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 9,
            Profile = ForceCompositionProfile.Garrison
        });

        Assert.Equal(["Heavy", "Line"], generated.Select(s => s.SquadTemplate.Name).ToArray());
        Assert.Equal(2, generated[1].Members.Count);
        Assert.Equal(9, SquadBattleValue(generated));
    }

    [Fact]
    public void GenericForce_UsesBestPartialSquadForRemainder()
    {
        SquadTemplate weak = CreateTemplate(1, "Weak", SquadTypes.None, 10, 20);
        SquadTemplate exact = CreateTemplate(2, "Exact", SquadTypes.None, 5, 45);
        Faction faction = CreateFaction(weak, exact);

        List<Squad> generated = ForceGenerator.GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 9,
            Profile = ForceCompositionProfile.AssaultForce
        });

        Squad squad = Assert.Single(generated);
        Assert.Equal("Exact", squad.SquadTemplate.Name);
        Assert.Single(squad.Members);
        Assert.Equal(9, SquadBattleValue(generated));
    }

    [Fact]
    public void GenericForce_ReturnsEmptyWhenMinimumSquadCannotFitBudget()
    {
        SquadTemplate line = CreateTemplate(1, "Line", SquadTypes.None, 5, 10, minSoldiers: 3);
        Faction faction = CreateFaction(line);

        List<Squad> generated = ForceGenerator.GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 5,
            Profile = ForceCompositionProfile.AssaultForce
        });

        Assert.Empty(generated);
    }

    [Fact]
    public void ScoutPatrol_GeneratesTierNumberOfScoutSquads()
    {
        SquadTemplate scout = CreateTemplate(1, "Scout", SquadTypes.Scout, 1, 2);
        SquadTemplate line = CreateTemplate(2, "Line", SquadTypes.None, 1, 2);
        Faction faction = CreateFaction(scout, line);

        List<Squad> generated = ForceGenerator.GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            Tier = 3,
            Profile = ForceCompositionProfile.ScoutPatrol
        });

        Assert.Equal(3, generated.Count);
        Assert.All(generated, squad => Assert.Equal("Scout", squad.SquadTemplate.Name));
    }

    [Fact]
    public void SpecialHqTarget_ClampsTierAndAddsBodyguardWhenBudgetIsZero()
    {
        SquadTemplate lesserHq = CreateTemplate(1, "Lesser HQ", SquadTypes.HQ, 1, 5);
        SquadTemplate greaterHq = CreateTemplate(2, "Greater HQ", SquadTypes.HQ, 1, 10);
        SquadTemplate bodyguard = CreateTemplate(3, "Bodyguard", SquadTypes.Bodyguard, 2, 4);
        greaterHq.BodyguardSquadTemplate = bodyguard;
        Faction faction = CreateFaction(lesserHq, greaterHq, bodyguard);

        List<Squad> generated = ForceGenerator.GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            Tier = 99,
            TargetBattleValue = 0,
            Profile = ForceCompositionProfile.SpecialHQTarget
        });

        Assert.Equal(["Greater HQ", "Bodyguard"], generated.Select(s => s.SquadTemplate.Name).ToArray());
    }

    [Fact]
    public void MinimumForceRequest_IsTheCheapestFullNonHqSquad()
    {
        SquadTemplate hq = CreateTemplate(1, "HQ", SquadTypes.HQ, 1, 2);
        SquadTemplate line = CreateTemplate(2, "Line", SquadTypes.None, 5, 10);
        SquadTemplate heavy = CreateTemplate(3, "Heavy", SquadTypes.None, 1, 25);
        SquadTemplate valueless = CreateTemplate(4, "Valueless", SquadTypes.None, 1, 0);
        Faction faction = CreateFaction(hq, line, heavy, valueless);

        // HQ squads and zero-value templates are not counted: the floor is the cheapest full
        // squad the generic generator could actually field.
        Assert.Equal(10, faction.MinimumForceRequest);
    }

    [Fact]
    public void MinimumForceRequest_IsZeroWithoutUsableTemplates()
    {
        Assert.Equal(0, CreateFaction().MinimumForceRequest);
    }

    private static SquadTemplate CreateTemplate(
        int id,
        string name,
        SquadTypes squadTypes,
        byte maxSoldiers,
        int battleValue,
        byte minSoldiers = 0)
    {
        // Squad battle value is now derived from its members, so back it out onto a per-squad
        // trooper whose value × the member count reproduces the intended squad battle value.
        SoldierTemplate trooper = new(
            id, TestModelFactory.HumanSpecies, name + " Trooper",
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

    private static long SquadBattleValue(IEnumerable<Squad> squads) =>
        squads.Sum(squad => squad.Members.Sum(member => (long)member.Template.BattleValue));

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
