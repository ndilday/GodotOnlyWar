using OnlyWar.Medical.Readiness;
using OnlyWar.Domain.Missions;
using OnlyWar.Operations.Orders;
using OnlyWar.Operations.Planetary;
using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Application
{
    public enum ForceTreeGrouping { Company, Ship }

    internal sealed record ForceTreeSquad(
        Squad Squad,
        string Origin,
        Ship Ship = null,
        SquadEligibilityExclusion Exclusion = SquadEligibilityExclusion.None,
        bool Assigned = false)
    {
        public bool Selectable => Exclusion == SquadEligibilityExclusion.None && !Assigned;
    }

    /// <summary>
    /// Strength and doctrine inputs for a force listing. They are supplied by the query that owns
    /// the session rather than read from the installed campaign, so a tree always describes the
    /// campaign its caller asked about.
    /// </summary>
    internal sealed record ForceTreeInputs(
        RecruitmentProgram Program = null,
        ChapterOperationalDoctrine Doctrine = null);

    internal static class PlanetaryForceTreeBuilder
    {
        private const string CharacterSquadGroupPrefix = "group:characters:squad:";
        private static readonly SquadRowViewModelBuilder RowBuilder = new();

        public static IReadOnlyList<HierarchyTreeItem> BuildCharacterGroup(
            IEnumerable<SpecialistOption> options,
            IReadOnlySet<int> selectedIds = null)
        {
            List<SpecialistOption> characters = (options ?? Enumerable.Empty<SpecialistOption>())
                .Where(option => option?.Soldier != null)
                .GroupBy(option => option.Soldier.Id)
                .Select(group => group.First())
                // SpecialistAvailability returns alphabetical rows, but this tree follows the
                // Chapter's order of battle: direct Chapter squads first, then each child Unit in
                // order (for example, 1st through 10th Company HQ).
                .OrderBy(option => ForceOrdering.UnitOrderKey(option.HomeSquad?.ParentUnit))
                .ThenBy(option => ForceOrdering.SquadTypeOrder(option.HomeSquad))
                .ThenBy(option => option.HomeSquad?.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(option => option.HomeSquad?.Id ?? int.MaxValue)
                .ThenBy(option => option.Soldier.Name)
                .ToList();
            if (characters.Count == 0) return [];

            IReadOnlySet<int> selected = selectedIds ?? new HashSet<int>();
            int available = characters.Count(option => option.IsAvailable);
            List<HierarchyTreeItem> children = characters
                .GroupBy(option => option.HomeSquad?.Id ?? -1)
                // GroupBy preserves the first-seen order established above, keeping the group
                // rows in the same depth-first sequence as their source character rows.
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
                badgeAccent: UiAccent.Muted,
                rowHeight: 32,
                collapsedByDefault: true)];
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
                iconKey: SquadIconKeys.For(first.HomeSquad?.SquadTemplate),
                badge: $"{available}/{options.Count} available",
                tooltip: $"Select individual characters from {squadName}.",
                selectable: available > 0,
                badgeAccent: UiAccent.Muted,
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
                badgeAccent: option.IsAvailable ? UiAccent.Body : UiAccent.Muted,
                rowHeight: 34);
        }

        public static IReadOnlyList<HierarchyTreeItem> Build(
            IReadOnlyList<ForceTreeSquad> roster,
            ForceTreeGrouping grouping,
            string filter,
            IReadOnlySet<int> selectedIds,
            ForceTreeInputs inputs = null)
        {
            inputs ??= new ForceTreeInputs();
            string normalized = string.IsNullOrWhiteSpace(filter)
                ? null : filter.Trim();
            List<ForceTreeSquad> filtered = (roster ?? [])
                .Where(item => item?.Squad != null)
                .Where(item => normalized == null || Matches(item, normalized))
                .OrderBy(item => ForceOrdering.UnitOrderKey(item.Squad.ParentUnit))
                .ThenBy(item => ForceOrdering.SquadTypeOrder(item.Squad))
                .ThenBy(item => item.Squad.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Squad.Id)
                .ToList();

            return filtered
                .GroupBy(item => grouping == ForceTreeGrouping.Ship
                    ? item.Ship?.Name ?? "No ship"
                    : item.Squad.ParentUnit?.Name ?? "Unassigned company")
                .Select(group => BuildGroup(group.Key, group.ToList(), grouping,
                    selectedIds ?? new HashSet<int>(), normalized == null, inputs))
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
            SquadEligibilityExclusion.Leaderless => "Formation requires a squad leader",
            SquadEligibilityExclusion.RequiredLeaderUnavailable => "Required squad leader is withheld or unavailable",
            SquadEligibilityExclusion.BelowMinimumDutyReadyStrength => "Below the Chapter's minimum duty-ready strength",
            SquadEligibilityExclusion.NoDutyReadyParticipants => "No duty-ready members are available",
            SquadEligibilityExclusion.DoctrineWithholding => "Members withheld by Chapter doctrine",
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
            bool collapsed,
            ForceTreeInputs inputs)
        {
            // HQs, administrative formations, and personnel pools are not mission-squad rows.
            // Keep them out of the company summary so they neither inflate the squad denominator
            // nor appear as unavailable mission capacity.
            List<ForceTreeSquad> summaryItems = items
                .Where(item => SpecialistAvailability.IsMissionSquadFormation(item.Squad))
                .ToList();
            int selected = summaryItems.Count(item => selectedIds.Contains(item.Squad.Id) || item.Assigned);
            int deployable = summaryItems.Count(item => item.Exclusion == SquadEligibilityExclusion.None
                || item.Assigned);
            int deployed = summaryItems.Count(item => IsDeployed(item, selectedIds));
            int available = summaryItems.Count(item => !IsDeployed(item, selectedIds)
                && item.Exclusion == SquadEligibilityExclusion.None);
            int undeployable = summaryItems.Count(item => !IsDeployed(item, selectedIds)
                && item.Exclusion != SquadEligibilityExclusion.None);
            string deployedText = $"{deployed} squad{(deployed == 1 ? "" : "s")} deployed";
            string status = $"{available} available, {undeployable} undeployable";
            bool allSelected = deployable > 0 && selected == deployable;
            string token = grouping == ForceTreeGrouping.Ship
                ? $"ship={items.First().Ship?.Id ?? -1}"
                : $"company={items.First().Squad.ParentUnit?.Id ?? -1}";
            return new HierarchyTreeItem(
                $"group:{token}",
                name.ToUpperInvariant(),
                items.Select(item => BuildSquad(item, selectedIds, inputs)).ToList(),
                grouping == ForceTreeGrouping.Ship ? "ship" : null,
                deployedText,
                $"Select every eligible squad in {name}.",
                items.Any(item => item.Selectable),
                // The group row is an action (select or clear all), but its highlight mirrors the
                // actionable child rows when the complete group is selected so that collapsed
                // groups retain a clear visual indication of their selection.
                isSelected: allSelected,
                UiAccent.Muted,
                rowHeight: 44,
                collapsedByDefault: collapsed,
                badgeLines: [deployedText, status]);
        }

        private static bool IsDeployed(ForceTreeSquad item, IReadOnlySet<int> selectedIds) =>
            item.Assigned
            || selectedIds.Contains(item.Squad.Id)
            || item.Squad.CurrentOrders != null;

        private static HierarchyTreeItem BuildSquad(
            ForceTreeSquad item,
            IReadOnlySet<int> selectedIds,
            ForceTreeInputs inputs)
        {
            Squad squad = item.Squad;
            SquadStrengthSnapshot strengthSnapshot = Strength(squad, inputs);
            int dutyReady = strengthSnapshot.DutyReady;
            string commitment = squad.CurrentOrders == null ? "Unassigned"
                : MissionAvailability.GetOrderLabel(squad.CurrentOrders.Mission);
            string tooltip = BuildSquadTooltip(item, dutyReady, strengthSnapshot.Full, commitment);
            string strength = $"{dutyReady}/{strengthSnapshot.Full}";
            string assignment = BuildAssignedSquadText(item);
            string badge = assignment
                ?? (item.Exclusion == SquadEligibilityExclusion.None
                    ? strength
                    : $"{ExclusionReason(item.Exclusion).ToUpperInvariant()} · {strength}");
            SquadRowContext rowContext = new(
                SquadRowContextKind.PlanetaryOperations,
                SquadRowAction.BeginOrder,
                isSelected: item.Assigned || selectedIds.Contains(squad.Id),
                isSelectable: item.Selectable,
                isEnabled: item.Selectable,
                contextBadge: assignment);
            SquadRowViewModel row = RowBuilder.Build(
                squad, rowContext, inputs.Program, inputs.Doctrine);
            return new HierarchyTreeItem(
                $"squad:{squad.Id}",
                squad.Name,
                iconKey: SquadIconKeys.For(squad.SquadTemplate),
                badge: badge,
                tooltip: tooltip,
                selectable: item.Selectable,
                isSelected: item.Assigned || selectedIds.Contains(squad.Id),
                badgeAccent: item.Exclusion == SquadEligibilityExclusion.None
                    ? UiAccent.Body : UiAccent.Muted,
                rowHeight: 34,
                squadRow: row);
        }

        private static SquadStrengthSnapshot Strength(Squad squad, ForceTreeInputs inputs) =>
            SquadStrengthSnapshotBuilder.Build(
                squad, program: inputs.Program, doctrine: inputs.Doctrine);

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

        internal static string BuildSquadTooltip(ForceTreeSquad item, ForceTreeInputs inputs = null)
        {
            Squad squad = item?.Squad;
            if (squad == null) return string.Empty;

            SquadStrengthSnapshot strengthSnapshot =
                Strength(squad, inputs ?? new ForceTreeInputs());
            string commitment = squad.CurrentOrders == null ? "Unassigned"
                : MissionAvailability.GetOrderLabel(squad.CurrentOrders.Mission);
            return BuildSquadTooltip(
                item, strengthSnapshot.DutyReady, strengthSnapshot.Full, commitment);
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
