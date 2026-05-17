using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Generation;

public class ForceGeneratorTests
{
    [Fact]
    public void GenericForce_ExcludesHqTemplatesAndFillsBudgetGreedily()
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

        Assert.Equal(["Heavy", "Line"], generated.Select(s => s.SquadTemplate.Name).ToArray());
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

    private static SquadTemplate CreateTemplate(int id, string name, SquadTypes squadTypes, byte maxSoldiers, int battleValue)
    {
        return new SquadTemplate(
            id,
            name,
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, maxSoldiers)],
            squadTypes,
            battleValue);
    }

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
