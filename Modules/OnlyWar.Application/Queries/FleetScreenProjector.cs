using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;

namespace OnlyWar.Application
{
    /// <summary>
    /// Builds the Classis transfer tree from live task forces. Ordering, warp concealment and the
    /// squad rows are decided here so the screen only renders ids and text.
    /// </summary>
    internal sealed class FleetScreenProjector
    {
        private static readonly SquadRowViewModelBuilder SquadRowBuilder = new();

        public IReadOnlyList<TreeNode> BuildPlayerFleets(
            IEnumerable<TaskForce> taskForces,
            Faction playerFaction,
            RecruitmentProgram recruitmentProgram,
            ChapterOperationalDoctrine doctrine)
        {
            if (playerFaction == null) return [];
            return (taskForces ?? [])
                .Where(taskForce => taskForce.Faction == playerFaction)
                .OrderBy(taskForce => taskForce.Id)
                .Select(taskForce => BuildFleetNode(
                    taskForce, recruitmentProgram, doctrine))
                .ToList();
        }

        public TreeNode BuildFleetNode(TaskForce taskForce, PlayerForce force)
            => BuildFleetNode(
                taskForce,
                force?.RecruitmentProgram,
                force?.Army?.OperationalDoctrine);

        private TreeNode BuildFleetNode(
            TaskForce taskForce,
            RecruitmentProgram recruitmentProgram,
            ChapterOperationalDoctrine doctrine)
        {
            // A task force in the Warp is out of contact: it, its ships, and the marines
            // aboard are listed for accounting but cannot be selected or inspected.
            bool isInWarp = taskForce.TravelPhase == FleetTravelPhase.InWarp;
            string status = DescribeFleetStatus(taskForce);
            List<TreeNode> shipNodes = taskForce.Ships
                .OrderByDescending(ship => ship.Template.SoldierCapacity)
                .ThenBy(ship => ship.Template.Id)
                .ThenBy(ship => ship.Name)
                .ThenBy(ship => ship.Id)
                .Select(ship =>
                {
                    string shipText =
                        $"{ship.Name} ({ship.LoadedSoldierCount}/{ship.Template.SoldierCapacity})";
                    List<TreeNode> squadNodes = isInWarp
                        ? []
                        : BuildLoadedUnitNodes(ship, recruitmentProgram, doctrine).ToList();
                    return new TreeNode(
                        ship.Id, shipText, squadNodes, selectable: !isInWarp, kind: TreeNodeKind.Ship);
                })
                .ToList();

            return new TreeNode(
                taskForce.Id,
                $"Task Force {taskForce.Id}: {status}",
                shipNodes,
                selectable: !isInWarp,
                kind: TreeNodeKind.Fleet);
        }

        public IReadOnlyList<TreeNode> BuildLoadedUnitNodes(Ship ship, PlayerForce force)
            => BuildLoadedUnitNodes(
                ship,
                force?.RecruitmentProgram,
                force?.Army?.OperationalDoctrine);

        private IReadOnlyList<TreeNode> BuildLoadedUnitNodes(
            Ship ship,
            RecruitmentProgram recruitmentProgram,
            ChapterOperationalDoctrine doctrine)
        {
            return ship.LoadedSquads
                .Where(squad => squad.IsPresentOperationalForce && squad.Members.Count > 0)
                .OrderBy(squad => ForceOrdering.UnitOrderKey(squad.ParentUnit))
                .ThenBy(ForceOrdering.SquadTypeOrder)
                .ThenBy(squad => squad.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(squad => squad.Id)
                .GroupBy(squad => squad.ParentUnit)
                .Select(group =>
                {
                    Unit unit = group.Key;
                    List<TreeNode> squadNodes = group
                        .Select(squad => new TreeNode(
                            squad.Id,
                            squad.Name,
                            [],
                            kind: TreeNodeKind.Squad,
                            squadRow: SquadRowBuilder.Build(
                                squad,
                                new SquadRowContext(
                                    SquadRowContextKind.Fleet,
                                    SquadRowAction.Inspect,
                                    isSelectable: true,
                                    isEnabled: true,
                                    contextBadge: "TRANSFER"),
                                recruitmentProgram,
                                doctrine)))
                        .ToList();
                    return new TreeNode(
                        unit?.Id ?? 0,
                        unit?.Name ?? "Unassigned Unit",
                        squadNodes,
                        selectable: false,
                        kind: TreeNodeKind.Unit);
                })
                .ToList();
        }

        public static string DescribeFleetStatus(TaskForce taskForce)
        {
            string destinationName = taskForce.Destination?.Name ?? "Unknown";
            return taskForce.TravelPhase switch
            {
                FleetTravelPhase.OutboundSystemTransit =>
                    $"Departing for {destinationName} ({taskForce.CurrentPhaseWeeksRemaining}w to warp translation)",
                FleetTravelPhase.InWarp => $"In Warp to {destinationName}",
                FleetTravelPhase.InboundSystemTransit =>
                    $"Arriving at {destinationName} ({taskForce.CurrentPhaseWeeksRemaining}w to orbit)",
                _ => taskForce.Planet != null
                    ? $"In orbit at {taskForce.Planet.Name}"
                    : "In transit"
            };
        }
    }
}
