using OnlyWar.Models.Fleets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public enum FleetCapacityPlanKind { NoRelocationRequired, Direct, Rebalance, Impossible }
    public sealed record FleetSquadMove(int SquadId, int? SourceShipId, int TargetShipId, int Headcount);
    public sealed record FleetCapacitySolution(
        FleetCapacityPlanKind Kind,
        IReadOnlyList<FleetSquadMove> Moves,
        IReadOnlyList<string> Blockers);

    public sealed class FleetCapacityPlanService
    {
        public FleetCapacitySolution PlanPlacement(Squad squad, IEnumerable<Ship> legalShips)
        {
            if (squad == null) throw new ArgumentNullException(nameof(squad));
            List<Ship> ships = legalShips?.Distinct().ToList() ?? [];
            if (squad.BoardedLocation == null || ships.Contains(squad.BoardedLocation))
                return new(FleetCapacityPlanKind.NoRelocationRequired, [], []);
            if (squad.CurrentOrders != null)
                return Impossible("Formation has an active order.");
            if (ships.Count == 0)
                return Impossible("No legal local ship is available.");
            if (ships.Any(ship => ship.Fleet?.TravelPhase == FleetTravelPhase.InWarp))
                ships = ships.Where(ship => ship.Fleet?.TravelPhase != FleetTravelPhase.InWarp).ToList();
            int headcount = squad.Members.Count;
            List<Ship> direct = ships.Where(ship => ship.AvailableCapacity >= headcount)
                .OrderBy(ship => ship.AvailableCapacity - headcount).ToList();
            if (direct.Count > 0)
            {
                return new(FleetCapacityPlanKind.Direct,
                    direct.Select(ship => new FleetSquadMove(
                        squad.Id, squad.BoardedLocation?.Id, ship.Id, headcount)).ToList(), []);
            }
            if (ships.Sum(ship => ship.AvailableCapacity) < headcount)
                return Impossible("Insufficient aggregate fleet capacity.");

            // Complete bounded search: every whole unrelated squad is either left in place or
            // assigned to one legal ship. Local fleets are deliberately small, keeping this exact.
            List<Squad> movable = ships.SelectMany(ship => ship.LoadedSquads)
                .Where(candidate => candidate != squad && candidate.CurrentOrders == null)
                .Distinct().OrderByDescending(candidate => candidate.Members.Count).ToList();
            List<FleetSquadMove> best = null;
            int bestMoved = int.MaxValue;
            Search(0, movable, ships, ships.ToDictionary(s => s.Id, s => s.AvailableCapacity), [],
                headcount, squad, ref best, ref bestMoved);
            return best == null
                ? Impossible("No ship can berth the required whole squad after local rebalancing.")
                : new(FleetCapacityPlanKind.Rebalance, best, []);
        }

        private static void Search(
            int index, IReadOnlyList<Squad> movable, IReadOnlyList<Ship> ships,
            Dictionary<int, int> free, List<FleetSquadMove> moves, int required,
            Squad target, ref List<FleetSquadMove> best, ref int bestMoved)
        {
            if (moves.Count >= bestMoved) return;
            Ship destination = ships.Where(ship => free[ship.Id] >= required)
                .OrderBy(ship => free[ship.Id] - required).FirstOrDefault();
            if (destination != null)
            {
                bestMoved = moves.Count;
                best = [.. moves, new FleetSquadMove(
                    target.Id, target.BoardedLocation?.Id, destination.Id, required)];
                return;
            }
            if (index >= movable.Count) return;
            Search(index + 1, movable, ships, free, moves, required, target, ref best, ref bestMoved);
            Squad squad = movable[index];
            Ship source = squad.BoardedLocation;
            foreach (Ship ship in ships.Where(ship => ship != source && free[ship.Id] >= squad.Members.Count))
            {
                free[source.Id] += squad.Members.Count;
                free[ship.Id] -= squad.Members.Count;
                moves.Add(new FleetSquadMove(squad.Id, source.Id, ship.Id, squad.Members.Count));
                Search(index + 1, movable, ships, free, moves, required, target, ref best, ref bestMoved);
                moves.RemoveAt(moves.Count - 1);
                free[source.Id] -= squad.Members.Count;
                free[ship.Id] += squad.Members.Count;
            }
        }

        private static FleetCapacitySolution Impossible(string blocker) =>
            new(FleetCapacityPlanKind.Impossible, [], [blocker]);
    }
}
