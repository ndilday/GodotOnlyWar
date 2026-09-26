using System;
using System.Linq;
using OnlyWar.Application;
using OnlyWar.Domain.Squads;
using Xunit;

namespace OnlyWar.Tests.Application;

public class ForceOrderingTests
{
    [Fact]
    public void FormationOrdinalOrder_SortsRomanNumeralsNumerically_ThenUnnumberedByName()
    {
        Squad[] squads =
        [
            Numbered(1, "IX Tactical Squad, 6 Co.", 9),
            Unnumbered(2, "Brakus Squad"),
            Numbered(3, "V Tactical Squad, 6 Co.", 5),
            Numbered(4, "IV Tactical Squad, 6 Co.", 4),
            Numbered(5, "X Tactical Squad, 6 Co.", 10),
            Numbered(6, "I Tactical Squad, 6 Co.", 1)
        ];

        string[] ordered = squads
            .OrderBy(ForceOrdering.FormationOrdinalOrder)
            .ThenBy(squad => squad.Name, StringComparer.OrdinalIgnoreCase)
            .Select(squad => squad.Name)
            .ToArray();

        Assert.Equal(
            [
                "I Tactical Squad, 6 Co.",
                "IV Tactical Squad, 6 Co.",
                "V Tactical Squad, 6 Co.",
                "IX Tactical Squad, 6 Co.",
                "X Tactical Squad, 6 Co.",
                "Brakus Squad"
            ],
            ordered);
    }

    private static Squad Numbered(int id, string name, int ordinal) =>
        new(id, name, null, null) { FormationOrdinal = ordinal };

    private static Squad Unnumbered(int id, string name) => new(id, name, null, null);
}
