using Godot;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Orders;
using OnlyWar.Helpers.UI;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.PlanetaryOperations
{
    public enum ForceTreeGrouping { Company, Ship }

    public sealed record ForceTreeSquad(
        Squad Squad,
        string Origin,
        Ship Ship = null,
        SquadEligibilityExclusion Exclusion = SquadEligibilityExclusion.None,
        bool Assigned = false)
    {
        public bool Selectable => Exclusion == SquadEligibilityExclusion.None && !Assigned;
    }

    public static class PlanetaryForceTreeBuilder
    {
        private const string CharacterSquadGroupPrefix = "group:characters:squad:";

        public static IReadOnlyList<HierarchyTreeItem> BuildCharacterGroup(
            IEnumerable<SpecialistOption> options,
            IReadOnlySet<int> selectedIds = null)
        {
            List<SpecialistOption> characters = (options ?? Enumerable.Empty<SpecialistOption>())
                .Where(option => option?.Soldier != null)
                .GroupBy(option => option.Soldier.Id)
                .Select(group => group.First())
                .OrderBy(option => option.HomeSquad?.Name)
                .ThenBy(option => option.Soldier.Name)
                .ToList();
            if (characters.Count == 0) return [];

            IReadOnlySet<int> selected = selectedIds ?? new HashSet<int>();
            int available = characters.Count(option => option.IsAvailable);
            List<HierarchyTreeItem> children = characters
                .GroupBy(option => option.HomeSquad?.Id ?? -1)
                .OrderBy(group => group.First().HomeSquad?.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Key)
                .Select(group => BuildCharacterSquadGroup(group.ToList(), selected))
                .ToList();
            return [new HierarchyTreeItem(
                "group:characters",
                "CHARACTERS",
                children,
                iconKey: "chapter",
                badge: $"{available}/{characters.Count} available",
                tooltip: "Select individual administrative characters as order or movement participants.",
                selectable: available > 0,
                badgeColor: OnlyWarStyle.MutedText,
                rowHeight: 32)];
        }

        public static IReadOnlyList<PlayerSoldier> ResolveCharacterSelection(
            IEnumerable<SpecialistOption> options,
            string key)
        {
            List<SpecialistOption> characters = (options ?? Enumerable.Empty<SpecialistOption>())
                .Where(option => option?.Soldier != null && option.IsAvailable)
                .ToList();
            if (key == "group:characters")
            {
                return characters.Select(option => option.Soldier)
                    .DistinctBy(soldier => soldier.Id).ToList();
            }
            if (key?.StartsWith(CharacterSquadGroupPrefix, StringComparison.Ordinal) == true
                && int.TryParse(key[CharacterSquadGroupPrefix.Length..], out int squadId))
            {
                return characters
                    .Where(option => (option.HomeSquad?.Id ?? -1) == squadId)
                    .Select(option => option.Soldier)
                    .DistinctBy(soldier => soldier.Id)
                    .ToList();
            }
            if (key?.StartsWith("character:", StringComparison.Ordinal) == true
                && int.TryParse(key[10..], out int soldierId))
            {
                return characters.Where(option => option.Soldier.Id == soldierId)
                    .Select(option => option.Soldier).ToList();
            }
            return [];
        }

        private static HierarchyTreeItem BuildCharacterSquadGroup(
            IReadOnlyList<SpecialistOption> options,
            IReadOnlySet<int> selected)
        {
            SpecialistOption first = options.First();
            string squadName = first.HomeSquad?.Name ?? "UNASSIGNED ADMINISTRATIVE SQUAD";
            int available = options.Count(option => option.IsAvailable);
            List<HierarchyTreeItem> characters = options
                .OrderBy(option => option.Soldier.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(option => option.Soldier.Id)
                .Select(option => BuildCharacterRow(option, selected))
                .ToList();
            return new HierarchyTreeItem(
                $"{CharacterSquadGroupPrefix}{first.HomeSquad?.Id ?? -1}",
                squadName.ToUpperInvariant(),
                characters,
                iconKey: IconAtlas.GetSquadIconKey(first.HomeSquad?.SquadTemplate),
                badge: $"{available}/{options.Count} available",
                tooltip: $"Select individual characters from {squadName}.",
                selectable: available > 0,
                badgeColor: OnlyWarStyle.MutedText,
                rowHeight: 32);
        }

        private static HierarchyTreeItem BuildCharacterRow(
            SpecialistOption option,
            IReadOnlySet<int> selected)
        {
            string location = option.StatusLabel ?? "No operational location";
            return new HierarchyTreeItem(
                $"character:{option.Soldier.Id}",
                option.Soldier.Name,
                iconKey: "chapter",
                badge: option.IsAvailable ? location : $"UNAVAILABLE · {location}",
                tooltip: option.IsAvailable
                    ? $"{option.Label}\nLocation: {location}"
                    : $"{option.Label}\nLocation: {location}\nReason: {option.Reason ?? "Unavailable"}",
                selectable: option.IsSelectable && option.IsAvailable,
                isSelected: selected.Contains(option.Soldier.Id),
                badgeColor: option.IsAvailable
                    ? OnlyWarStyle.BodyText : OnlyWarStyle.MutedText,
                rowHeight: 34);
        }

        public static IReadOnlyList<HierarchyTreeItem> Build(
            IReadOnlyList<ForceTreeSquad> roster,
            ForceTreeGrouping grouping,
            string filter,
            IReadOnlySet<int> selectedIds)
        {
            string normalized = string.IsNullOrWhiteSpace(filter)
                ? null : filter.Trim();
            List<ForceTreeSquad> filtered = (roster ?? [])
                .Where(item => item?.Squad != null)
                .Where(item => normalized == null || Matches(item, normalized))
                .OrderBy(item => item.Squad.ParentUnit == null
                    ? "\uffff" : FleetScreenController.GetUnitOrderKey(item.Squad.ParentUnit))
                .ThenBy(item => FleetScreenController.GetSquadTypeOrder(item.Squad))
                .ThenBy(item => item.Squad.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Squad.Id)
                .ToList();

            return filtered
                .GroupBy(item => grouping == ForceTreeGrouping.Ship
                    ? item.Ship?.Name ?? "No ship"
                    : item.Squad.ParentUnit?.Name ?? "Unassigned company")
                .Select(group => BuildGroup(group.Key, group.ToList(), grouping,
                    selectedIds ?? new HashSet<int>(), normalized == null))
                .ToList();
        }

        public static IReadOnlyList<Squad> ResolveSelection(
            IReadOnlyList<ForceTreeSquad> roster,
            string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return [];
            if (key.StartsWith("squad:", StringComparison.Ordinal)
                && int.TryParse(key[6..], out int squadId))
            {
                return (roster ?? []).Where(item => item.Squad?.Id == squadId)
                    .Select(item => item.Squad).DistinctBy(squad => squad.Id).ToList();
            }
            if (key.StartsWith("group:", StringComparison.Ordinal))
            {
                string token = key[6..];
                return (roster ?? []).Where(item => GroupToken(item, token))
                    .Select(item => item.Squad).DistinctBy(squad => squad.Id).ToList();
            }
            return [];
        }

        public static string ExclusionReason(SquadEligibilityExclusion exclusion) => exclusion switch
        {
            SquadEligibilityExclusion.Embarked => "Aboard ship",
            SquadEligibilityExclusion.OutOfArea => "Outside the target and adjacent regions",
            SquadEligibilityExclusion.NonOperational => "Formation is non-operational",
            SquadEligibilityExclusion.EmptyFormation => "Formation has no members",
            SquadEligibilityExclusion.PersonnelPool => "Personnel pool; attach individuals instead",
            SquadEligibilityExclusion.ProcedureBlocked => "A member is reserved for a Chapter procedure",
            SquadEligibilityExclusion.AssignedElsewhere => "Committed to another order",
            SquadEligibilityExclusion.MissionUnavailable => "Mission is unavailable from this origin",
            _ => "Eligible"
        };

        private static HierarchyTreeItem BuildGroup(
            string name,
            List<ForceTreeSquad> items,
            ForceTreeGrouping grouping,
            IReadOnlySet<int> selectedIds,
            bool collapsed)
        {
            int effective = items.Sum(item => SoldierPresenceService.DeployableCount(item.Squad));
            int selected = items.Count(item => selectedIds.Contains(item.Squad.Id) || item.Assigned);
            string badge = selected == 0 ? $"{items.Count} sq · {effective} effective"
                : selected == items.Count ? $"ALL · {effective} effective"
                : $"{selected}/{items.Count} selected · {effective} effective";
            string token = grouping == ForceTreeGrouping.Ship
                ? $"ship={items.First().Ship?.Id ?? -1}"
                : $"company={items.First().Squad.ParentUnit?.Id ?? -1}";
            return new HierarchyTreeItem(
                $"group:{token}",
                name.ToUpperInvariant(),
                items.Select(item => BuildSquad(item, selectedIds)).ToList(),
                grouping == ForceTreeGrouping.Ship ? "ship" : null,
                badge,
                $"Select every eligible squad in {name}.",
                items.Any(item => item.Selectable),
                // The group row is an action (select or clear all), not an independent selection.
                // Keep its visual state neutral so a stale parent outline cannot imply that the
                // company itself is selected after individual squad choices change.
                isSelected: false,
                OnlyWarStyle.MutedText,
                rowHeight: 32,
                collapsedByDefault: collapsed);
        }

        private static HierarchyTreeItem BuildSquad(
            ForceTreeSquad item,
            IReadOnlySet<int> selectedIds)
        {
            Squad squad = item.Squad;
            int present = SoldierPresenceService.PresentCount(squad);
            int effective = SoldierPresenceService.DeployableCount(squad);
            string commitment = squad.CurrentOrders == null ? "Unassigned"
                : MissionAvailability.GetOrderLabel(squad.CurrentOrders.Mission);
            string tooltip = BuildSquadTooltip(item, effective, present, commitment);
            string strength = $"{effective}/{present}";
            string assignment = BuildAssignedSquadText(item);
            string badge = assignment
                ?? (item.Exclusion == SquadEligibilityExclusion.None
                    ? strength
                    : $"{ExclusionReason(item.Exclusion).ToUpperInvariant()} · {strength}");
            return new HierarchyTreeItem(
                $"squad:{squad.Id}",
                squad.Name,
                iconKey: IconAtlas.GetSquadIconKey(squad.SquadTemplate),
                badge: badge,
                tooltip: tooltip,
                selectable: item.Selectable,
                isSelected: item.Assigned || selectedIds.Contains(squad.Id),
                badgeColor: item.Exclusion == SquadEligibilityExclusion.None
                    ? OnlyWarStyle.BodyText : OnlyWarStyle.MutedText,
                rowHeight: 34);
        }

        private static string BuildAssignedSquadText(ForceTreeSquad item)
        {
            Squad squad = item?.Squad;
            if (squad == null) return null;

            Order order = item.Assigned || item.Exclusion == SquadEligibilityExclusion.AssignedElsewhere
                ? squad.CurrentOrders : null;
            if (order?.Mission == null) return null;

            string orderName = MissionAvailability.GetOrderLabel(order.Mission);
            string regionName = squad.CurrentRegion?.Name
                ?? item.Origin
                ?? order.Mission.Region?.Name;
            if (string.IsNullOrWhiteSpace(orderName) || string.IsNullOrWhiteSpace(regionName))
            {
                return null;
            }

            return $"{orderName}, {regionName}";
        }

        internal static string BuildSquadTooltip(ForceTreeSquad item)
        {
            Squad squad = item?.Squad;
            if (squad == null) return string.Empty;

            int healthy = SoldierPresenceService.DeployableCount(squad);
            int total = SoldierPresenceService.PresentCount(squad);
            string commitment = squad.CurrentOrders == null ? "Unassigned"
                : MissionAvailability.GetOrderLabel(squad.CurrentOrders.Mission);
            return BuildSquadTooltip(item, healthy, total, commitment);
        }

        private static string BuildSquadTooltip(
            ForceTreeSquad item,
            int healthy,
            int total,
            string commitment)
        {
            Squad squad = item.Squad;
            string location = squad.BoardedLocation?.Name
                ?? squad.CurrentRegion?.Name
                ?? item.Origin
                ?? "Unknown";
            return string.Join("\n", [
                $"Leader: {squad.SquadLeader?.Name ?? "None"}",
                $"Squad Size: {healthy}/{total}",
                $"Commitment: {commitment}",
                $"Location: {location}"
            ]);
        }

        private static bool Matches(ForceTreeSquad item, string filter) =>
            (item.Squad.Name?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
            || (item.Squad.SquadTemplate?.Name?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
            || (item.Squad.ParentUnit?.Name?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
            || (item.Ship?.Name?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
            || (item.Origin?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);

        private static bool GroupToken(ForceTreeSquad item, string token) =>
            token == $"ship={item.Ship?.Id ?? -1}"
            || token == $"company={item.Squad.ParentUnit?.Id ?? -1}";
    }
}
