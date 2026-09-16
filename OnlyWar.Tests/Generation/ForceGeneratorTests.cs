using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Generation.World;
using OnlyWar.Domain;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using RuntimeTacticalEntityIdAllocator = OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator;
using SequentialEntityIdAllocator = OnlyWar.Runtime.Allocators.SequentialEntityIdAllocator;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Generation;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class ForceGeneratorTests
{
    [Fact]
    public void GeneratedForce_UsesOneInjectedRandomStreamInExistingDrawOrder()
    {
        SquadTemplate line = CreateTemplate(1, "Line", SquadTypes.None, 1, 1);
        Faction faction = CreateFaction(line);
        var random = new RecordingRng();

        List<Squad> generated = ForceGenerator.GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 1,
            Profile = ForceCompositionProfile.AssaultForce
        }, random, new RuntimeTacticalEntityIdAllocator());

        Assert.Single(generated);
        Assert.Equal(
            new[] { "int" }.Concat(Enumerable.Repeat("normal", 11)),
            random.Draws);
    }

    [Fact]
    public void TacticalForce_UsesUniqueNegativeIdsOutsidePersistentIdRange()
    {
        SquadTemplate line = CreateTemplate(1, "Line", SquadTypes.None, 2, 2);
        Faction faction = CreateFaction(line);

        List<Squad> generated = ForceGenerator.GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 4,
            Profile = ForceCompositionProfile.AssaultForce
        }, new RecordingRng(), new RuntimeTacticalEntityIdAllocator());

        int[] entityIds = generated
            .SelectMany(squad => new[] { squad.Id }.Concat(squad.Members.Select(member => member.Id)))
            .ToArray();

        Assert.Equal(6, entityIds.Length);
        Assert.Equal(entityIds.Length, entityIds.Distinct().Count());
        Assert.All(entityIds, id => Assert.True(id < -1));
        Assert.DoesNotContain(42, entityIds);
    }

    [Fact]
    public void TacticalForce_IdsIncreaseInCreationOrderForBattleTieBreakers()
    {
        SquadTemplate line = CreateTemplate(1, "Line", SquadTypes.None, 2, 2);
        Faction faction = CreateFaction(line);

        List<Squad> generated = ForceGenerator.GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 4,
            Profile = ForceCompositionProfile.AssaultForce
        }, new RecordingRng(), new RuntimeTacticalEntityIdAllocator());

        int[] entityIds = generated
            .SelectMany(squad => new[] { squad.Id }.Concat(squad.Members.Select(member => member.Id)))
            .ToArray();

        Assert.Equal(entityIds.OrderBy(id => id), entityIds);
        Assert.All(entityIds, id => Assert.True(id < -1));
    }

    [Fact]
    public void TacticalForce_UsesReadableCreationOrderNamesInsteadOfNegativeIds()
    {
        SquadTemplate line = CreateTemplate(1, "Line", SquadTypes.None, 2, 2);
        Faction faction = CreateFaction(line);

        List<Squad> generated = ForceGenerator.GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 4,
            Profile = ForceCompositionProfile.AssaultForce
        }, new RecordingRng(), new RuntimeTacticalEntityIdAllocator());

        ISoldier[] soldiers = generated.SelectMany(squad => squad.Members).ToArray();
        Assert.Equal(
            Enumerable.Range(1, soldiers.Length)
                .Select(index => $"{soldiers[index - 1].Template.Name} {index}"),
            soldiers.Select(soldier => soldier.Name));
        Assert.DoesNotContain(
            soldiers,
            soldier => soldier.Name.Contains(
                soldier.Id.ToString(),
                System.StringComparison.Ordinal));
    }

    [Fact]
    public void TacticalForce_DoesNotAdvancePersistentSquadOrSoldierCounters()
    {
        SquadTemplate line = CreateTemplate(1, "Line", SquadTypes.None, 1, 1);
        Faction faction = CreateFaction(line);
        var random = new RecordingRng();
        var persistentIds = new SequentialEntityIdAllocator(500_000);
        Squad persistentBefore = SquadFactory.GenerateSquad(line, random, persistentIds);

        ForceGenerator.GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 3,
            Profile = ForceCompositionProfile.AssaultForce
        }, random, new RuntimeTacticalEntityIdAllocator());

        Squad persistentAfter = SquadFactory.GenerateSquad(line, random, persistentIds);

        Assert.Equal(persistentBefore.Id + 2, persistentAfter.Id);
        Assert.Equal(
            persistentBefore.Members.Single().Id + 2,
            persistentAfter.Members.Single().Id);
    }

    [Fact]
    public void GenericForce_DoesNotAddHqBelowCommandScaleAndFillsBudget()
    {
        SquadTemplate hq = CreateTemplate(1, "HQ", SquadTypes.HQ, 1, 5);
        SquadTemplate heavy = CreateTemplate(2, "Heavy", SquadTypes.None, 1, 7);
        SquadTemplate line = CreateTemplate(3, "Line", SquadTypes.None, 1, 3);
        Faction faction = CreateFaction(hq, heavy, line);

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
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

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
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

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
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

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
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

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
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

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
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

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
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

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
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

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
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

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            Tier = 3,
            TargetBattleValue = 6,
            Profile = ForceCompositionProfile.ScoutPatrol
        });

        Assert.Equal(3, generated.Count);
        Assert.All(generated, squad => Assert.Equal("Scout", squad.SquadTemplate.Name));
    }

    [Fact]
    public void ScoutPatrol_WithoutATierIsBoundedByItsBudget()
    {
        // A patrol screen supplies no Tier. It used to loop on Tier alone, so it generated nothing
        // at all and every patrol order in the game was silently dropped.
        SquadTemplate scout = CreateTemplate(1, "Scout", SquadTypes.Scout, 10, 100, minSoldiers: 4);
        Faction faction = CreateFaction(scout);

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 250,
            Profile = ForceCompositionProfile.ScoutPatrol
        });

        Assert.Equal(2, generated.Count);
        Assert.All(generated, squad => Assert.Equal(10, squad.Members.Count));
    }

    [Fact]
    public void ScoutPatrol_SendsAnUnderstrengthPartyRatherThanNothing()
    {
        // Below the price of a full squad, an undersized scouting party beats no party at all.
        SquadTemplate scout = CreateTemplate(1, "Scout", SquadTypes.Scout, 10, 100, minSoldiers: 4);
        Faction faction = CreateFaction(scout);

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            Tier = 1,
            TargetBattleValue = 70,
            Profile = ForceCompositionProfile.ScoutPatrol
        });

        Squad party = Assert.Single(generated);
        Assert.Equal(7, party.Members.Count);
    }

    [Fact]
    public void ScoutPatrol_DoesNotAddARemnantBehindAFullSquad()
    {
        // The fallback fires only when the force would otherwise be empty. A full squad plus an
        // understrength remnant is not what a probe or a screen wants, so the leftover is left.
        SquadTemplate scout = CreateTemplate(1, "Scout", SquadTypes.Scout, 10, 100, minSoldiers: 4);
        Faction faction = CreateFaction(scout);

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 150,
            Profile = ForceCompositionProfile.ScoutPatrol
        });

        Squad party = Assert.Single(generated);
        Assert.Equal(10, party.Members.Count);
    }

    [Fact]
    public void ScoutPatrol_FieldsNothingBelowTheMinimumSquad()
    {
        SquadTemplate scout = CreateTemplate(1, "Scout", SquadTypes.Scout, 10, 100, minSoldiers: 4);
        Faction faction = CreateFaction(scout);

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            Tier = 1,
            TargetBattleValue = 39,
            Profile = ForceCompositionProfile.ScoutPatrol
        });

        Assert.Empty(generated);
    }

    [Fact]
    public void GeneratedForce_NamesRepeatedTemplatesWithPhoneticDesignators()
    {
        SquadTemplate line = CreateTemplate(1, "Line", SquadTypes.None, 1, 1);
        Faction faction = CreateFaction(line);

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 3,
            Profile = ForceCompositionProfile.AssaultForce
        });

        Assert.Equal(["Line Alpha", "Line Bravo", "Line Charlie"], generated.Select(squad => squad.Name).ToArray());
    }

    [Fact]
    public void GeneratedForce_UsesTemplateNameWhenTemplateAppearsOnce()
    {
        SquadTemplate line = CreateTemplate(1, "Line", SquadTypes.None, 1, 1);
        Faction faction = CreateFaction(line);

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = 1,
            Profile = ForceCompositionProfile.AssaultForce
        });

        Assert.Equal("Line", Assert.Single(generated).Name);
    }

    [Fact]
    public void SpecialHqTarget_ClampsTierAndAddsBodyguardWhenBudgetIsZero()
    {
        SquadTemplate lesserHq = CreateTemplate(1, "Lesser HQ", SquadTypes.HQ, 1, 5);
        SquadTemplate greaterHq = CreateTemplate(2, "Greater HQ", SquadTypes.HQ, 1, 10);
        SquadTemplate bodyguard = CreateTemplate(3, "Bodyguard", SquadTypes.Bodyguard, 2, 4);
        greaterHq.BodyguardSquadTemplate = bodyguard;
        Faction faction = CreateFaction(lesserHq, greaterHq, bodyguard);

        List<Squad> generated = GenerateForce(new ForceGenerationRequest
        {
            Faction = faction,
            Tier = 99,
            TargetBattleValue = 0,
            Profile = ForceCompositionProfile.SpecialHQTarget
        });

        Assert.Equal(["Greater HQ", "Bodyguard"], generated.Select(s => s.SquadTemplate.Name).ToArray());
    }

    [Fact]
    public void MinimumForceRequest_IsTheCheapestUnderstrengthNonHqSquad()
    {
        // Line is five troopers at 2 BV each, fieldable down to two of them.
        SquadTemplate hq = CreateTemplate(1, "HQ", SquadTypes.HQ, 1, 2, minSoldiers: 1);
        SquadTemplate line = CreateTemplate(2, "Line", SquadTypes.None, 5, 10, minSoldiers: 2);
        SquadTemplate heavy = CreateTemplate(3, "Heavy", SquadTypes.None, 1, 25, minSoldiers: 1);
        SquadTemplate valueless = CreateTemplate(4, "Valueless", SquadTypes.None, 1, 0, minSoldiers: 1);
        Faction faction = CreateFaction(hq, line, heavy, valueless);

        // HQ squads and zero-value templates are not counted. The floor is what the generator
        // must be given to produce anything at all - the cheapest squad at MINIMUM strength, not
        // the cheapest one at full strength, which would be Line's 10.
        Assert.Equal(4, faction.MinimumForceRequest);
    }

    [Fact]
    public void MinimumForceRequest_IsNotRaisedByWideningATemplatesMaximum()
    {
        // The regression this guards: pricing the floor off the FULL squad meant that allowing a
        // template to field more soldiers raised the budget a faction needed before it could issue
        // any order at all, so making squads more flexible made the faction less able to act.
        SquadTemplate narrow = CreateTemplate(1, "Narrow", SquadTypes.None, 10, 50, minSoldiers: 5);
        SquadTemplate wide = CreateTemplate(2, "Wide", SquadTypes.None, 20, 100, minSoldiers: 5);

        Assert.Equal(
            CreateFaction(narrow).MinimumForceRequest,
            CreateFaction(wide).MinimumForceRequest);
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
        byte minSoldiers = 0,
        bool rollsStrength = false)
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
            [new SquadTemplateElement(
                trooper, minSoldiers, maxSoldiers, rollsStrength: rollsStrength)],
            squadTypes);
    }

    private static long SquadBattleValue(IEnumerable<Squad> squads) =>
        squads.Sum(squad => squad.Members.Sum(member => (long)member.Template.BattleValue));

    private static List<Squad> GenerateForce(ForceGenerationRequest request) =>
        ForceGenerator.GenerateForce(request, new StaticRNG());

    private static Faction CreateFaction(params SquadTemplate[] squadTemplates)
    {
        return new Faction(
            1,
            "Test Faction",
            Color.Black,
            false,
            false,
            FactionBehavior.None,
            GrowthType.None,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate> { [TestModelFactory.MarineTemplate.Id] = TestModelFactory.MarineTemplate },
            squadTemplates.ToDictionary(st => st.Id),
            new Dictionary<int, UnitTemplate>(),
            null,
            null,
            null);
    }

    private sealed class RecordingRng : IRNG
    {
        public List<string> Draws { get; } = [];

        public double GetDoubleInRange(double lowerBound, double upperBound) =>
            throw new System.NotSupportedException();

        public double GetLinearDouble() => throw new System.NotSupportedException();

        public int GetIntBelowMax(int min, int max)
        {
            Draws.Add("int");
            return min;
        }

        public double NextRandomZValue()
        {
            Draws.Add("normal");
            return 0;
        }
    }
}
