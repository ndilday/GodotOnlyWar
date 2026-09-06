using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public enum MusterMutationKind
    {
        Transfer,
        PromotionAndAssignment,
        TransferAndRoleChange,
        PromotionAndCreateFormation,
        FleetRebalance
    }

    public sealed record MusterStagedAction(
        Guid ActionId,
        int SoldierId,
        int SourceSquadId,
        int? TargetSquadId,
        SoldierTemplate TargetTemplate,
        MusterMutationKind Kind,
        string SourceDisplay,
        string TargetDisplay,
        Unit ProvisionalUnit = null,
        SquadTemplate ProvisionalSquadTemplate = null,
        int? PlannedShipId = null,
        Guid? ProvisionalFormationId = null);

    public sealed record MusterPlanValidation(bool IsValid, IReadOnlyList<string> Blockers)
    {
        public static MusterPlanValidation Valid { get; } = new(true, []);
    }

    public sealed record MusterCommitResult(bool Succeeded, IReadOnlyList<string> Errors);

    /// <summary>
    /// Owns a stable, editable draft. No domain mutation occurs until Commit, and Commit first
    /// revalidates every action against live state so a Fleet detour cannot partially apply stale work.
    /// </summary>
    public sealed class MusterPlanService
    {
        private readonly List<MusterStagedAction> _actions = [];
        private readonly SoldierTransferService _transferService = new();
        public IReadOnlyList<MusterStagedAction> Actions => _actions;

        public bool IsStaged(int soldierId) => _actions.Any(action => action.SoldierId == soldierId);

        public MusterStagedAction Stage(
            PlayerSoldier soldier,
            SoldierTransferOption option)
        {
            if (soldier?.AssignedSquad == null) throw new ArgumentException("A posted soldier is required.", nameof(soldier));
            if (option == null) throw new ArgumentNullException(nameof(option));
            if (IsStaged(soldier.Id)) throw new InvalidOperationException("This soldier already has a staged change.");

            MusterMutationKind kind = option.IsNewSquad
                ? MusterMutationKind.PromotionAndCreateFormation
                : option.SoldierTemplate.Rank > soldier.Template.Rank
                    ? MusterMutationKind.PromotionAndAssignment
                    : option.SoldierTemplate != soldier.Template
                        ? MusterMutationKind.TransferAndRoleChange
                        : MusterMutationKind.Transfer;
            Guid actionId = Guid.NewGuid();
            MusterStagedAction action = new(
                actionId, soldier.Id, soldier.AssignedSquad.Id,
                option.IsNewSquad || option.IsProvisionalSquad ? null : option.SquadId,
                option.SoldierTemplate, kind,
                $"{soldier.Template.Name}, {soldier.AssignedSquad.Name}",
                option.DisplayName,
                option.TargetUnit,
                option.TargetSquadTemplate,
                ProvisionalFormationId: option.IsNewSquad
                    ? actionId
                    : option.ProvisionalFormationId);
            _actions.Add(action);
            return action;
        }

        public bool Undo(Guid actionId)
        {
            MusterStagedAction action = _actions.FirstOrDefault(candidate => candidate.ActionId == actionId);
            return action != null && _actions.Remove(action);
        }

        public void Clear() => _actions.Clear();

        public (int Outgoing, int Incoming) GetStrengthDelta(int squadId) =>
            (_actions.Count(action => action.SourceSquadId == squadId && action.TargetSquadId != squadId),
             _actions.Count(action => action.TargetSquadId == squadId && action.SourceSquadId != squadId));

        public MusterPlanValidation Validate(
            PlayerForce force,
            SoldierTransferContext context = null)
        {
            List<string> blockers = [];
            if (force?.Army == null) return new(false, ["Chapter roster is unavailable."]);
            force.Army.PopulateSquadMap();
            context ??= _transferService.CreateContext(force.Army.OrderOfBattle);
            foreach (MusterStagedAction action in _actions)
            {
                if (!force.Army.PlayerSoldierMap.TryGetValue(action.SoldierId, out PlayerSoldier soldier)
                    || soldier.AssignedSquad?.Id != action.SourceSquadId)
                {
                    blockers.Add($"Soldier {action.SoldierId}'s posting changed while the plan was open.");
                    continue;
                }
                SoldierTransferOption option = ToOption(action);
                bool stillOffered = action.ProvisionalFormationId.HasValue
                    && action.Kind != MusterMutationKind.PromotionAndCreateFormation
                    ? IsProvisionalDestinationStillOffered(action, soldier)
                    : _transferService.GetTransferOptions(context, soldier)
                        .Any(candidate => SameDestination(candidate, option));
                if (!stillOffered)
                {
                    blockers.Add($"{soldier.Name} is no longer eligible for {action.TargetDisplay}.");
                    continue;
                }
                if (_transferService.WouldExceedShipCapacity(soldier, option, force.Army.SquadMap))
                {
                    blockers.Add($"{action.TargetDisplay} has unresolved transport capacity.");
                }
            }
            return blockers.Count == 0 ? MusterPlanValidation.Valid : new(false, blockers);
        }

        public MusterCommitResult Commit(PlayerForce force, Date date)
        {
            SoldierTransferContext context = force?.Army?.OrderOfBattle == null
                ? null
                : _transferService.CreateContext(force.Army.OrderOfBattle);
            MusterPlanValidation validation = Validate(force, context);
            if (!validation.IsValid) return new(false, validation.Blockers);

            // Validation above is deliberately complete before the first mutation. ApplyTransfer
            // only returns false after that if an invariant changes synchronously during commit.
            Dictionary<Guid, Squad> provisionalSquads = [];
            foreach (MusterStagedAction action in _actions.ToList())
            {
                PlayerSoldier soldier = force.Army.PlayerSoldierMap[action.SoldierId];
                SoldierTransferOption option;
                if (action.Kind == MusterMutationKind.PromotionAndCreateFormation)
                {
                    HashSet<Squad> before = action.ProvisionalUnit.Squads.ToHashSet();
                    option = ToOption(action);
                    if (!_transferService.ApplyTransfer(
                            soldier, option, force.Army.SquadMap, date))
                    {
                        throw new InvalidOperationException(
                            $"Validated Muster action {action.ActionId} could not be committed.");
                    }
                    Squad created = action.ProvisionalUnit.Squads
                        .FirstOrDefault(squad => !before.Contains(squad));
                    if (created == null || !action.ProvisionalFormationId.HasValue)
                    {
                        throw new InvalidOperationException(
                            $"Validated provisional formation {action.ActionId} was not created.");
                    }
                    provisionalSquads[action.ProvisionalFormationId.Value] = created;
                    continue;
                }

                if (action.ProvisionalFormationId.HasValue)
                {
                    if (!provisionalSquads.TryGetValue(
                            action.ProvisionalFormationId.Value, out Squad provisionalSquad))
                    {
                        throw new InvalidOperationException(
                            $"Provisional formation {action.ProvisionalFormationId} is unavailable.");
                    }
                    option = new(
                        provisionalSquad.Id,
                        action.TargetTemplate,
                        action.TargetDisplay);
                }
                else
                {
                    option = ToOption(action);
                }

                if (!_transferService.ApplyTransfer(soldier, option, force.Army.SquadMap, date))
                {
                    throw new InvalidOperationException(
                        $"Validated Muster action {action.ActionId} could not be committed.");
                }
            }
            _actions.Clear();
            return new(true, []);
        }

        private bool IsProvisionalDestinationStillOffered(
            MusterStagedAction action,
            PlayerSoldier soldier)
        {
            MusterStagedAction creation = _actions.FirstOrDefault(candidate =>
                candidate.ActionId == action.ProvisionalFormationId.Value
                && candidate.Kind == MusterMutationKind.PromotionAndCreateFormation);
            if (creation == null)
            {
                return false;
            }

            List<SoldierTemplate> projectedMembers = _actions
                .Where(candidate => candidate.ProvisionalFormationId == action.ProvisionalFormationId)
                .TakeWhile(candidate => candidate.ActionId != action.ActionId)
                .Select(candidate => candidate.TargetTemplate)
                .ToList();
            return _transferService.GetProvisionalSquadOpenings(
                    creation.ProvisionalSquadTemplate, projectedMembers, soldier)
                .Contains(action.TargetTemplate);
        }

        private static SoldierTransferOption ToOption(MusterStagedAction action) => new(
            action.TargetSquadId ?? 0,
            action.TargetTemplate,
            action.TargetDisplay,
            IsNewSquad: action.Kind == MusterMutationKind.PromotionAndCreateFormation,
            TargetUnit: action.ProvisionalUnit,
            TargetSquadTemplate: action.ProvisionalSquadTemplate,
            IsProvisionalSquad: action.ProvisionalFormationId.HasValue
                && action.Kind != MusterMutationKind.PromotionAndCreateFormation,
            ProvisionalFormationId: action.Kind == MusterMutationKind.PromotionAndCreateFormation
                ? null
                : action.ProvisionalFormationId);

        private static bool SameDestination(SoldierTransferOption left, SoldierTransferOption right) =>
            left.IsNewSquad == right.IsNewSquad
            && left.IsProvisionalSquad == right.IsProvisionalSquad
            && left.SquadId == right.SquadId
            && left.SoldierTemplate == right.SoldierTemplate
            && left.TargetUnit == right.TargetUnit
            && left.TargetSquadTemplate == right.TargetSquadTemplate
            && (!left.IsProvisionalSquad
                || left.ProvisionalFormationId == right.ProvisionalFormationId);
    }
}
