using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OnlyWar.Helpers
{
    /// <summary>
    /// Deterministic flagship identity and succession. Administrative station relocation is kept
    /// in AdministrativeStationService so ship selection and station mutation cannot drift apart.
    /// </summary>
    public sealed class FlagshipService
    {
        public Ship SelectInitialFlagship(Faction playerFaction, IEnumerable<Ship> ships)
        {
            Ship flagship = CandidateShips(playerFaction, ships)
                .OrderByDescending(ship => ship.Template?.FlagshipPrecedence ?? 0)
                .ThenByDescending(ship => ship.Template?.HullSize ?? 0)
                .ThenByDescending(ship => ship.Template?.SoldierCapacity ?? 0)
                .ThenBy(ship => ship.Id)
                .FirstOrDefault();
            if (flagship == null)
            {
                throw new InvalidOperationException("The player fleet contains no ship for a flagship.");
            }
            SetFlagship(playerFaction, ships, flagship);
            return flagship;
        }

        public Ship EnsureSinglePlayerFlagship(Faction playerFaction, IEnumerable<Ship> ships)
        {
            List<Ship> candidates = CandidateShips(playerFaction, ships).ToList();
            List<Ship> marked = candidates.Where(ship => ship.IsFlagship).ToList();
            if (marked.Count > 1)
            {
                throw new InvalidDataException("The save contains multiple player flagships.");
            }
            if (marked.Count == 1) return marked[0];
            return SelectInitialFlagship(playerFaction, candidates);
        }

        public Ship SelectSuccessor(Faction playerFaction, IEnumerable<Ship> survivingShips)
        {
            Ship successor = FindSuccessor(playerFaction, survivingShips);
            SetFlagship(playerFaction, survivingShips, successor);
            return successor;
        }

        public Ship FindSuccessor(Faction playerFaction, IEnumerable<Ship> survivingShips)
        {
            Ship successor = CandidateShips(playerFaction, survivingShips)
                .OrderByDescending(ship => ship.Template?.FlagshipPrecedence ?? 0)
                .ThenByDescending(ship => ship.Template?.HullSize ?? 0)
                .ThenByDescending(ship => ship.Template?.SoldierCapacity ?? 0)
                .ThenBy(ship => ship.Id)
                .FirstOrDefault();
            if (successor == null)
            {
                throw new InvalidOperationException("No surviving player ship can become flagship.");
            }
            return successor;
        }

        public void ValidateSinglePlayerFlagship(Faction playerFaction, IEnumerable<Ship> ships)
        {
            List<Ship> marked = CandidateShips(playerFaction, ships)
                .Where(ship => ship.IsFlagship)
                .ToList();
            if (marked.Count != 1)
            {
                throw new InvalidDataException(
                    $"Expected exactly one player flagship, found {marked.Count}.");
            }
        }

        public void SetFlagship(Faction playerFaction, IEnumerable<Ship> ships, Ship flagship)
        {
            if (flagship == null || !CandidateShips(playerFaction, ships).Contains(flagship))
            {
                throw new InvalidOperationException("The selected flagship is not a player ship.");
            }
            foreach (Ship ship in CandidateShips(playerFaction, ships))
            {
                ship.IsFlagship = ReferenceEquals(ship, flagship);
            }
        }

        private static IEnumerable<Ship> CandidateShips(
            Faction playerFaction,
            IEnumerable<Ship> ships) =>
            (ships ?? Enumerable.Empty<Ship>())
                .Where(ship => ship != null
                    && (playerFaction == null || ship.Fleet?.Faction == playerFaction));
    }
}
