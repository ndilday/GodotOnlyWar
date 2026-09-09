using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OnlyWar.Persistence.Database.GameState;

/// <summary>
/// Validates invariants owned by the save representation while it is still being read. It does
/// not select or mutate the active campaign and has no dependency on Campaign fleet policy.
/// </summary>
internal static class SaveStateValidator
{
    internal static void ValidateSinglePlayerFlagship(
        Faction playerFaction,
        IEnumerable<Ship> ships)
    {
        List<Ship> marked = (ships ?? [])
            .Where(ship => ship != null
                && (playerFaction == null || ship.Fleet?.Faction == playerFaction))
            .Where(ship => ship.IsFlagship)
            .ToList();
        if (marked.Count != 1)
        {
            throw new InvalidDataException(
                $"Expected exactly one player flagship, found {marked.Count}.");
        }
    }
}
