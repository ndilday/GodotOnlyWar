using OnlyWar.Runtime.Allocators;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using System.Collections.Generic;
using Xunit;

namespace OnlyWar.Tests.Domain;

// Loaded entities keep their persisted identity. Runtime-created entities consume the
// session allocator, so loading one graph cannot mutate another graph's identity stream.
public class EntityIdAllocationTests
{
    [Fact]
    public void Squad_RuntimeIdComesFromTheExplicitSessionAllocator()
    {
        SquadTemplate template = CreateSquadTemplate();
        Unit unit = CreateUnit();
        PersistentIdAllocator identity = new(nextSquadId: 100_002);

        _ = new Squad(100_000, "Loaded Squad", unit, template);
        Squad boundary = new(100_001, "Boundary Squad", unit, template);

        Squad runtime = new("Runtime Squad", unit, template, identity);

        Assert.NotEqual(boundary.Id, runtime.Id);
        Assert.True(runtime.Id > boundary.Id, $"Runtime squad reused id {runtime.Id}.");
    }

    [Fact]
    public void Unit_RuntimeIdComesFromTheExplicitSessionAllocator()
    {
        UnitTemplate template = new(1, "Test Unit Template", true, [], []);
        PersistentIdAllocator identity = new(nextUnitId: 100_002);

        _ = new Unit(100_000, "Loaded Unit", template, []);
        Unit boundary = new(100_001, "Boundary Unit", template, []);

        Unit runtime = new("Runtime Unit", template, identity);

        Assert.NotEqual(boundary.Id, runtime.Id);
        Assert.True(runtime.Id > boundary.Id, $"Runtime unit reused id {runtime.Id}.");
    }

    private static Unit CreateUnit()
    {
        UnitTemplate template = new(1, "Test Unit Template", true, [], []);
        return new Unit(1, "Test Unit", template, []);
    }

    private static SquadTemplate CreateSquadTemplate()
    {
        return new SquadTemplate(
            1,
            "Test Squad",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            new List<SquadTemplateElement>
            {
                new(TestModelFactory.SergeantTemplate, 0, 1),
                new(TestModelFactory.MarineTemplate, 0, 4)
            },
            SquadTypes.None);
    }
}
