using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using System.Collections.Generic;
using Xunit;

namespace OnlyWar.Tests.Domain;

// Squad and Unit both hand out runtime ids from a static counter that the loading
// constructor advances past whatever a save contains. The counter must move past an id
// that lands exactly on it, or the next runtime-created entity reuses that id — which
// surfaced as a duplicate-key crash the first time anything keyed the order of battle by
// squad id (SoldierTransferService.GetTransferOptions) after a new squad was created.
public class EntityIdAllocationTests
{
    [Fact]
    public void Squad_LoadedIdEqualToTheCounterStillAdvancesIt()
    {
        SquadTemplate template = CreateSquadTemplate();
        Unit unit = CreateUnit();

        // The first load pushes the counter to 100_001; the second load lands exactly on it.
        _ = new Squad(100_000, "Loaded Squad", unit, template);
        Squad boundary = new(100_001, "Boundary Squad", unit, template);

        Squad runtime = new("Runtime Squad", unit, template);

        Assert.NotEqual(boundary.Id, runtime.Id);
        Assert.True(runtime.Id > boundary.Id, $"Runtime squad reused id {runtime.Id}.");
    }

    [Fact]
    public void Unit_LoadedIdEqualToTheCounterStillAdvancesIt()
    {
        UnitTemplate template = new(1, "Test Unit Template", true, [], []);

        _ = new Unit(100_000, "Loaded Unit", template, []);
        Unit boundary = new(100_001, "Boundary Unit", template, []);

        Unit runtime = new("Runtime Unit", template);

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
