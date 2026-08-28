using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public enum MusterPopulationMode { PromotionEligible, AnyLegalMove }
    public enum FormationVacancyGroup { NeedsLeaders, Understrength, EmptyFormations, AvailableNewFormations }

    public sealed record HonorBadgeModel(string Name, string Type, ushort Level);

    public sealed record MusterCandidateViewModel(
        int SoldierId, string Name, string Role, string Formation, string Company,
        string Location, string SquadIconKey, IReadOnlyList<HonorBadgeModel> Honors, bool IsStaged);

    public sealed record FormationVacancyViewModel(
        FormationVacancyGroup Group, string GroupLabel, string FormationName,
        string StateLabel, string TypeLabel, string SquadIconKey, string RosterText,
        string RosterTooltip, string Location, string ResultingRole,
        SoldierTransferOption Option, int OrganizationOrder, int FormationOrdinal,
        bool IsPlanProjection = false, string SelectionKey = null, bool IsFull = false);

    public sealed class ChapterMusterViewModelBuilder
    {
        private readonly SoldierTransferService _transfers = new();

        public IReadOnlyList<MusterCandidateViewModel> BuildCandidates(
            PlayerForce force,
            MusterPlanService plan,
            MusterPopulationMode mode,
            IEnumerable<PlayerSoldier> scope = null,
            SoldierTransferContext context = null)
        {
            if (force?.Army?.OrderOfBattle == null) return [];
            context ??= _transfers.CreateContext(force.Army.OrderOfBattle);
            IEnumerable<PlayerSoldier> population = scope ?? force.Army.PlayerSoldierMap.Values;
            return population
                .Where(soldier => soldier.AssignedSquad != null)
                .Where(soldier => _transfers.HasLegalTransferOption(
                    context,
                    soldier,
                    mode == MusterPopulationMode.PromotionEligible))
                .OrderBy(soldier => GetUnitOrder(soldier.AssignedSquad.ParentUnit))
                .ThenBy(soldier => soldier.AssignedSquad.FormationOrdinal ?? int.MaxValue)
                .ThenBy(soldier => soldier.Name, StringComparer.OrdinalIgnoreCase)
                .Select(soldier => new MusterCandidateViewModel(
                    soldier.Id,
                    soldier.Name,
                    soldier.Template.Name,
                    soldier.AssignedSquad.Name,
                    soldier.AssignedSquad.ParentUnit?.Name ?? "Unassigned",
                    SquadLocationFormatter.Format(soldier.AssignedSquad),
                    IconKey(soldier.AssignedSquad),
                    soldier.SoldierAwards
                        .GroupBy(award => award.Type)
                        .Select(group => group
                            .OrderByDescending(award => award.Level)
                            .ThenByDescending(award => award.DateAwarded)
                            .First())
                        // Keep badge positions stable across soldiers. Sorting by tier here made
                        // the row layout depend on which honors happened to be earned first or
                        // which one had the highest grade.
                        .OrderBy(award => HonorTypeOrder(award.Type))
                        .ThenBy(award => award.Type, StringComparer.Ordinal)
                        .Select(award => new HonorBadgeModel(
                            award.Name ?? award.Type, award.Type, award.Level))
                        .Take(6).ToList(),
                    plan?.IsStaged(soldier.Id) == true))
                .ToList();
        }

        private static int HonorTypeOrder(string type) => type switch
        {
            AwardTypes.Gun => 0,
            AwardTypes.Sword => 1,
            AwardTypes.Voice => 2,
            AwardTypes.Banner => 3,
            _ => 4
        };

        public IReadOnlyList<FormationVacancyViewModel> BuildFormations(
            PlayerForce force,
            PlayerSoldier candidate,
            MusterPlanService plan,
            SoldierTransferContext context = null)
        {
            if (force?.Army?.OrderOfBattle == null || candidate == null) return [];
            context ??= _transfers.CreateContext(force.Army.OrderOfBattle);
            force.Army.PopulateSquadMap();
            Dictionary<int, int> unitOrder = force.Army.OrderOfBattle.ChildUnits
                .Select((unit, index) => (unit.Id, index))
                .ToDictionary(item => item.Id, item => item.index);
            List<FormationVacancyViewModel> rows = [];
            Dictionary<Unit, HashSet<int>> reservedOrdinals = [];

            // A staged creation has no live Squad yet, so it cannot appear in the order-of-battle
            // transfer context. Project each provisional formation with its complete staged
            // roster, and offer the selected candidate any remaining legal role slot in that same
            // future squad.
            List<MusterStagedAction> stagedActions = plan?.Actions.ToList() ?? [];
            foreach (MusterStagedAction creation in stagedActions.Where(action =>
                action.Kind == MusterMutationKind.PromotionAndCreateFormation
                && action.ProvisionalUnit != null
                && action.ProvisionalSquadTemplate != null))
            {
                Guid formationId = creation.ProvisionalFormationId ?? creation.ActionId;
                List<MusterStagedAction> formationActions = stagedActions
                    .Where(action =>
                        (action.ProvisionalFormationId
                            ?? (action.Kind == MusterMutationKind.PromotionAndCreateFormation
                                ? action.ActionId : Guid.Empty)) == formationId)
                    .ToList();
                int proposed = GetNextProvisionalOrdinal(
                    creation.ProvisionalUnit, creation.ProvisionalSquadTemplate, reservedOrdinals);
                string proposedName = ProposedDesignation(
                    creation.ProvisionalUnit, creation.ProvisionalSquadTemplate, proposed);
                List<SoldierTemplate> projectedMembers = formationActions
                    .Select(action => action.TargetTemplate)
                    .ToList();
                SoldierTemplate nextTemplate = plan?.IsStaged(candidate.Id) != true
                    ? _transfers.GetProvisionalSquadOpenings(
                            creation.ProvisionalSquadTemplate, projectedMembers, candidate)
                        .FirstOrDefault()
                    : null;
                SoldierTransferOption provisionalOption = nextTemplate == null
                    ? null
                    : new SoldierTransferOption(
                        0,
                        nextTemplate,
                        $"{nextTemplate.Name}, {proposedName}, {creation.ProvisionalUnit.Name}",
                        IsProvisionalSquad: true,
                        TargetUnit: creation.ProvisionalUnit,
                        TargetSquadTemplate: creation.ProvisionalSquadTemplate,
                        ProvisionalFormationId: formationId);
                string incomingNames = string.Join(
                    "\n",
                    formationActions.Select(action =>
                    {
                        PlayerSoldier member = force.Army.PlayerSoldierMap
                            .GetValueOrDefault(action.SoldierId);
                        return $"Incoming: {member?.Name ?? $"Soldier {action.SoldierId}"}";
                    }));
                if (provisionalOption != null)
                {
                    incomingNames += $"\nAvailable: {candidate.Name}";
                }
                string rosterText = $"0 +{formationActions.Count}";
                int capacity = Capacity(creation.ProvisionalSquadTemplate);
                rows.Add(new(
                    FormationVacancyGroup.Understrength, "UNDERSTRENGTH",
                    proposedName, "UNDERSTRENGTH", creation.ProvisionalSquadTemplate.Name,
                    IconKey(creation.ProvisionalSquadTemplate),
                    $"{rosterText} / {capacity}",
                    incomingNames,
                    SquadLocationFormatter.Format(
                        force.Army.PlayerSoldierMap.GetValueOrDefault(creation.SoldierId)?.AssignedSquad),
                    provisionalOption?.SoldierTemplate.Name ?? creation.TargetTemplate.Name,
                    provisionalOption,
                    unitOrder.GetValueOrDefault(creation.ProvisionalUnit.Id, int.MaxValue), proposed,
                    IsPlanProjection: true,
                    SelectionKey: $"staged:{formationId}",
                    IsFull: formationActions.Count >= capacity));
            }

            foreach (SoldierTransferOption option in _transfers.GetTransferOptions(context, candidate))
            {
                if (option.IsNewSquad)
                {
                    bool alreadyStaged = plan?.Actions.Any(action =>
                        action.SoldierId == candidate.Id
                        && action.Kind == MusterMutationKind.PromotionAndCreateFormation
                        && action.TargetTemplate == option.SoldierTemplate
                        && action.ProvisionalUnit == option.TargetUnit
                        && action.ProvisionalSquadTemplate == option.TargetSquadTemplate) == true;
                    if (alreadyStaged)
                    {
                        continue;
                    }
                    int proposed = GetNextProvisionalOrdinal(
                        option.TargetUnit, option.TargetSquadTemplate, reservedOrdinals);
                    string proposedName = ProposedDesignation(option.TargetUnit, option.TargetSquadTemplate, proposed);
                    rows.Add(new(
                        FormationVacancyGroup.AvailableNewFormations, "AVAILABLE NEW FORMATIONS",
                        proposedName, "NEW FORMATION", option.TargetSquadTemplate.Name,
                        "formation_create", $"0 / {Capacity(option.TargetSquadTemplate)}",
                        $"Incoming: {candidate.Name}", "—", option.SoldierTemplate.Name, option,
                        unitOrder.GetValueOrDefault(option.TargetUnit.Id, int.MaxValue), proposed));
                    continue;
                }
                if (!force.Army.SquadMap.TryGetValue(option.SquadId, out Squad squad)) continue;
                (int outgoing, int incoming) = plan?.GetStrengthDelta(squad.Id) ?? (0, 0);
                bool empty = squad.Members.Count == 0;
                bool needsLeader = squad.SquadLeader == null;
                bool isFull = squad.Members.Count - outgoing + incoming
                    >= Capacity(squad.SquadTemplate);
                FormationVacancyGroup group = empty
                    ? FormationVacancyGroup.EmptyFormations
                    : needsLeader ? FormationVacancyGroup.NeedsLeaders : FormationVacancyGroup.Understrength;
                string state = empty ? "EMPTY LINEAGE" : needsLeader ? "NEEDS LEADER" : "UNDERSTRENGTH";
                rows.Add(new(
                    group, GroupLabel(group), squad.Name, state, squad.SquadTemplate.Name,
                    empty ? "squad_lineage" : IconKey(squad), FormatStrength(squad.Members.Count, outgoing, incoming,
                        Capacity(squad.SquadTemplate)),
                    DeltaTooltip(plan, squad.Id, force),
                    empty ? "—" : SquadLocationFormatter.Format(squad),
                    option.SoldierTemplate.Name, option,
                    unitOrder.GetValueOrDefault(squad.ParentUnit?.Id ?? -1, int.MaxValue),
                    squad.FormationOrdinal ?? int.MaxValue,
                    IsFull: isFull));
            }
            return rows.OrderBy(row => row.Group).ThenBy(row => row.OrganizationOrder)
                .ThenBy(row => row.FormationOrdinal).ThenBy(row => row.FormationName).ToList();
        }

        private static int GetNextProvisionalOrdinal(
            Unit unit,
            SquadTemplate squadTemplate,
            IDictionary<Unit, HashSet<int>> reservedOrdinals)
        {
            int ordinal = FormationOrdinalAllocator.GetNextOrdinal(unit, squadTemplate);
            if (!reservedOrdinals.TryGetValue(unit, out HashSet<int> reserved))
            {
                reserved = [];
                reservedOrdinals[unit] = reserved;
            }

            while (reserved.Contains(ordinal)) ordinal++;
            reserved.Add(ordinal);
            return ordinal;
        }

        public static string FormatStrength(int current, int outgoing, int incoming, int maximum)
        {
            string result = current.ToString();
            if (outgoing > 0) result += $" -{outgoing}";
            if (incoming > 0) result += $" +{incoming}";
            return $"{result} / {maximum}";
        }

        private static string DeltaTooltip(MusterPlanService plan, int squadId, PlayerForce force)
        {
            if (plan == null) return string.Empty;
            List<string> lines = [];
            foreach (MusterStagedAction action in plan.Actions.Where(action =>
                action.SourceSquadId == squadId || action.TargetSquadId == squadId))
            {
                string name = force.Army.PlayerSoldierMap.GetValueOrDefault(action.SoldierId)?.Name
                    ?? $"Soldier {action.SoldierId}";
                lines.Add(action.SourceSquadId == squadId ? $"Outgoing: {name}" : $"Incoming: {name}");
            }
            return string.Join("\n", lines);
        }

        private static string ProposedDesignation(Unit unit, SquadTemplate template, int ordinal)
        {
            Squad preview = new(-1, template.Name, unit, template) { FormationOrdinal = ordinal };
            return SquadDesignationFormatter.IsNumberedLineFormation(preview)
                ? SquadDesignationFormatter.Format(preview)
                : $"New {template.Name}";
        }

        private static string GroupLabel(FormationVacancyGroup group) => group switch
        {
            FormationVacancyGroup.NeedsLeaders => "NEEDS LEADERS",
            FormationVacancyGroup.Understrength => "UNDERSTRENGTH",
            FormationVacancyGroup.EmptyFormations => "EMPTY FORMATIONS",
            _ => "AVAILABLE NEW FORMATIONS"
        };

        private static int Capacity(SquadTemplate template) =>
            template?.Elements?.Sum(element => element.MaximumNumber) ?? 0;
        private static int GetUnitOrder(Unit unit) => unit?.ParentUnit?.ChildUnits?.IndexOf(unit) ?? int.MaxValue;
        private static string IconKey(Squad squad) => IconKey(squad?.SquadTemplate);
        private static string IconKey(SquadTemplate template)
        {
            SquadTypes type = template?.SquadType ?? SquadTypes.None;
            if ((type & SquadTypes.HQ) != 0) return "hq";
            if ((type & SquadTypes.Scout) != 0) return "scout";
            if ((type & SquadTypes.Elite) != 0) return "elite";
            if ((type & SquadTypes.Fast) != 0) return "assault";
            if ((type & SquadTypes.Heavy) != 0) return "devastator";
            if ((type & SquadTypes.Bodyguard) != 0) return "bodyguard";
            return "tactical";
        }
    }
}
