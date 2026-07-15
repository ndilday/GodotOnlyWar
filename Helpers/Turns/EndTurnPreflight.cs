using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    public enum EndTurnWarningCategory
    {
        IdleDeployableSquads,
        ActionableTaskForces,
        SpecialMissionOpportunities
    }

    public sealed class EndTurnAttentionItem
    {
        public EndTurnWarningCategory Category { get; }
        public int EntityId { get; }
        public string Title { get; }
        public string Detail { get; }

        public EndTurnAttentionItem(
            EndTurnWarningCategory category,
            int entityId,
            string title,
            string detail)
        {
            Category = category;
            EntityId = entityId;
            Title = title ?? string.Empty;
            Detail = detail ?? string.Empty;
        }
    }

    public sealed class EndTurnPreflightReport
    {
        private readonly IReadOnlyList<EndTurnAttentionItem> _items;

        public IReadOnlyList<EndTurnAttentionItem> Items => _items;
        public bool RequiresConfirmation => _items.Count > 0;

        public EndTurnPreflightReport(IEnumerable<EndTurnAttentionItem> items)
        {
            _items = (items ?? Enumerable.Empty<EndTurnAttentionItem>()).ToList().AsReadOnly();
        }

        public IReadOnlyList<EndTurnAttentionItem> ForCategory(EndTurnWarningCategory category)
        {
            return _items.Where(item => item.Category == category).ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Finds campaign state that merits a conditional pause before turn resolution. It deliberately
    /// reports only actions the player can take now, and never creates an unconditional confirmation.
    /// </summary>
    public static class EndTurnPreflight
    {
        public static EndTurnPreflightReport Evaluate(
            Sector sector,
            Settings.EndTurnWarningPreferences preferences)
        {
            if (sector == null)
            {
                throw new ArgumentNullException(nameof(sector));
            }

            preferences ??= new Settings.EndTurnWarningPreferences();
            List<EndTurnAttentionItem> items = [];
            List<Squad> playerSquads = GetPlayerSquads(sector).ToList();

            if (preferences.WarnIdleDeployableSquads)
            {
                items.AddRange(playerSquads
                    .Where(IsIdleDeployableSquad)
                    .OrderBy(squad => squad.CurrentRegion?.Planet?.Name
                        ?? squad.BoardedLocation?.Fleet?.Planet?.Name)
                    .ThenBy(squad => squad.CurrentRegion?.Name
                        ?? squad.BoardedLocation?.Name)
                    .ThenBy(squad => squad.Name)
                    .Select(BuildSquadItem));
            }

            if (preferences.WarnActionableTaskForces)
            {
                items.AddRange(sector.Fleets.Values
                    .Where(fleet => IsActionableTaskForceWithoutOrders(sector, fleet))
                    .OrderBy(fleet => fleet.Planet?.Name)
                    .ThenBy(fleet => fleet.Id)
                    .Select(BuildTaskForceItem));
            }

            if (preferences.WarnSpecialMissionOpportunities)
            {
                HashSet<int> assignedMissionIds = playerSquads
                    .Select(squad => squad.CurrentOrders)
                    .Where(order => order?.Mission != null)
                    .Select(order => order.Mission.Id)
                    .ToHashSet();

                items.AddRange(sector.Planets.Values
                    .SelectMany(planet => planet.Regions.Where(region => region != null))
                    .SelectMany(region => region.SpecialMissions)
                    .Where(mission => mission != null && !assignedMissionIds.Contains(mission.Id))
                    .OrderBy(mission => mission.RegionFaction?.Region?.Planet?.Name)
                    .ThenBy(mission => mission.RegionFaction?.Region?.Name)
                    .ThenBy(mission => mission.MissionType)
                    .ThenBy(mission => mission.Id)
                    .Select(BuildSpecialMissionItem));
            }

            return new EndTurnPreflightReport(items);
        }

        public static string GetCategoryTitle(EndTurnWarningCategory category)
        {
            return category switch
            {
                EndTurnWarningCategory.IdleDeployableSquads => "Idle deployed squads",
                EndTurnWarningCategory.ActionableTaskForces => "Task forces awaiting orders",
                EndTurnWarningCategory.SpecialMissionOpportunities => "Opportunities at risk",
                _ => "Unresolved attention"
            };
        }

        public static string GetPreferenceLabel(EndTurnWarningCategory category)
        {
            return category switch
            {
                EndTurnWarningCategory.IdleDeployableSquads => "Warn about idle deployed squads",
                EndTurnWarningCategory.ActionableTaskForces => "Warn about task forces without destinations",
                EndTurnWarningCategory.SpecialMissionOpportunities => "Warn about unassigned special missions",
                _ => "Warn about this category"
            };
        }

        private static IEnumerable<Squad> GetPlayerSquads(Sector sector)
        {
            return sector.PlayerForce?.Army?.OrderOfBattle?.GetAllSquads()
                ?? Enumerable.Empty<Squad>();
        }

        private static bool IsIdleDeployableSquad(Squad squad)
        {
            bool canDeployFromCurrentLocation = squad?.CurrentRegion != null
                || squad?.BoardedLocation?.Fleet is
                {
                    TravelPhase: FleetTravelPhase.InOrbit,
                    Planet: not null
                };

            return squad?.Faction?.IsPlayerFaction == true
                && squad.CurrentOrders == null
                && canDeployFromCurrentLocation
                && squad.Members.Any(member => member.CanFight);
        }

        private static bool IsActionableTaskForceWithoutOrders(Sector sector, TaskForce fleet)
        {
            return fleet != null
                && fleet.Faction == sector.PlayerForce?.Faction
                && fleet.TravelPhase == FleetTravelPhase.InOrbit
                && fleet.Planet != null
                && fleet.Destination == null
                && fleet.Ships.Count > 0;
        }

        private static EndTurnAttentionItem BuildSquadItem(Squad squad)
        {
            string unit = string.IsNullOrWhiteSpace(squad.ParentUnit?.Name)
                ? string.Empty
                : $" - {squad.ParentUnit.Name}";
            int combatReady = squad.Members.Count(member => member.CanFight);
            string location = SquadLocationFormatter.Format(squad);
            return new EndTurnAttentionItem(
                EndTurnWarningCategory.IdleDeployableSquads,
                squad.Id,
                $"{squad.Name}{unit}",
                $"{combatReady}/{squad.Members.Count} combat-ready in {location}; no orders are assigned.");
        }

        private static EndTurnAttentionItem BuildTaskForceItem(TaskForce fleet)
        {
            int embarkedSquads = fleet.Ships.Sum(ship => ship.LoadedSquads.Count);
            string embarked = embarkedSquads == 0
                ? "no squads embarked"
                : $"{embarkedSquads} squad{(embarkedSquads == 1 ? string.Empty : "s")} embarked";
            return new EndTurnAttentionItem(
                EndTurnWarningCategory.ActionableTaskForces,
                fleet.Id,
                $"Task Force {fleet.Id}",
                $"{fleet.Ships.Count} ship{(fleet.Ships.Count == 1 ? string.Empty : "s")}, {embarked}, "
                + $"orbiting {fleet.Planet.Name}; no destination is plotted.");
        }

        private static EndTurnAttentionItem BuildSpecialMissionItem(Mission mission)
        {
            Region region = mission.RegionFaction?.Region;
            string location = region == null
                ? "an unknown location"
                : $"{region.Name}, {region.Planet?.Name ?? "unknown planet"}";
            bool noVisibleIntel = region?.GetPlayerVisibleIntel() <= 0f;
            string risk = noVisibleIntel
                ? "Regional intelligence is already zero, so this opportunity will be cleared when the turn advances."
                : "This opportunity has an independent 25% chance to disappear when the turn advances; "
                  + "it is also lost if regional intelligence falls to zero.";

            return new EndTurnAttentionItem(
                EndTurnWarningCategory.SpecialMissionOpportunities,
                mission.Id,
                $"{mission.MissionType} opportunity",
                $"{location}. {risk}");
        }
    }
}
