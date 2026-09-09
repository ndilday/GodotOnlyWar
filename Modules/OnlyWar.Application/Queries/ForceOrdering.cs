using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Application;

/// <summary>
/// The Chapter's own order of battle sequence, used by every force listing so a squad appears in
/// the same place on every screen. It reads only the unit graph, so it belongs with the projections
/// rather than with whichever screen happened to need it first.
/// </summary>
public static class ForceOrdering
{
    public static string UnitOrderKey(Unit unit)
    {
        if (unit == null) return "zzzzzzzz";

        Stack<string> segments = [];
        Unit current = unit;
        while (current != null)
        {
            Unit parent = current.ParentUnit;
            if (parent == null)
            {
                segments.Push($"root:{current.Name}:{current.Id:D8}");
                break;
            }

            int index = parent.ChildUnits?.IndexOf(current) ?? -1;
            segments.Push(index >= 0 ? $"{index:D8}" : $"unknown:{current.Name}:{current.Id:D8}");
            current = parent;
        }

        return string.Join("/", segments);
    }

    public static int SquadTypeOrder(Squad squad)
    {
        if (squad?.ParentUnit?.Squads == null) return int.MaxValue;

        List<Squad> orderedSquads = squad.ParentUnit.Squads.ToList();
        int index = orderedSquads.FindIndex(candidate =>
            candidate.SquadTemplate?.Id == squad.SquadTemplate?.Id);
        return index >= 0 ? index : int.MaxValue;
    }
}
