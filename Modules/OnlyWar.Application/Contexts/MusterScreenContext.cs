using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Abstractions;
using OnlyWar.Domain;
using OnlyWar.Domain;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Units;

namespace OnlyWar.Application;

internal sealed class MusterScreenContext
{
    private readonly Sector _sector;
    private readonly GameRulesData _rules;
    private readonly Date _currentDate;
    private readonly IPersistentIdAllocator _identity;

    private const string StaleSessionMessage = "The campaign changed. Reopen the Muster screen.";

    private readonly MusterPlanService _musterPlan = new();
    private readonly SoldierFilterService _musterFilters = new();
    private ChapterMusterViewModelBuilder _musterBuilder;

    internal bool HasChapter => TryGetChapter() != null;

    internal MusterScreenContext(
        Sector sector,
        GameRulesData rules,
        Date currentDate,
        IPersistentIdAllocator identity)
    {
        _sector = sector;
        _rules = rules;
        _currentDate = currentDate;
        _identity = identity;
    }

    private ChapterMusterViewModelBuilder MusterBuilder =>
        _musterBuilder ??= new ChapterMusterViewModelBuilder(_rules?.AwardCatalog);

    internal int StagedActionCount => _musterPlan.Actions.Count;

    internal bool IsStaged(int soldierId) => _musterPlan.IsStaged(soldierId);

    internal IReadOnlyList<MusterScopeOption> QueryMusterScopes()
    {
        Unit chapter = TryGetChapter();
        if (chapter == null) return [];

        List<MusterScopeOption> scopes =
        [
            new MusterScopeOption(0, $"ENTIRE CHAPTER · {chapter.GetAllMembers().Count()}")
        ];
        scopes.AddRange(chapter.ChildUnits.Select(company => new MusterScopeOption(
            company.Id, $"{company.Name} · {company.GetAllMembers().Count()}")));
        return scopes;
    }

    internal ChapterFilterOptions QueryMusterFilterOptions(int? scopeCompanyId)
    {
        PlayerForce force = _sector.PlayerForce;
        if (force?.Army == null) return new ChapterFilterOptions([], []);

        List<PlayerSoldier> scope = MusterScope(force, scopeCompanyId).ToList();
        GameRulesData rules = _rules;
        return new ChapterFilterOptions(
            _musterFilters.GetAvailableRoles(scope),
            _musterFilters.GetAvailableHonors(
                scope, rules.RatingAwardTiers, rules.AwardCatalog, rules.RatingConsumers));
    }

    internal IReadOnlyList<MusterCandidateViewModel> QueryMusterCandidates(
        int? scopeCompanyId,
        MusterPopulationMode mode,
        IReadOnlyList<SoldierFilterCondition> filters)
    {
        PlayerForce force = _sector.PlayerForce;
        if (force?.Army?.OrderOfBattle == null) return [];

        List<PlayerSoldier> scoped = MusterScope(force, scopeCompanyId).ToList();
        if (filters is { Count: > 0 })
        {
            scoped = _musterFilters.Apply(
                    scoped,
                    filters.ToList(),
                    _currentDate,
                    _rules.RatingConsumers)
                .OfType<PlayerSoldier>().ToList();
        }

        // Staged candidates leave the list: the plan already accounts for them.
        return MusterBuilder
            .BuildCandidates(force, _musterPlan, mode, scoped, MusterTransferContext(force))
            .Where(candidate => !candidate.IsStaged)
            .ToList();
    }

    internal IReadOnlyList<MusterFormationRow> QueryMusterFormations(int soldierId)
    {
        PlayerForce force = _sector.PlayerForce;
        PlayerSoldier soldier = FindMusterSoldier(soldierId);
        if (force?.Army == null || soldier == null) return [];

        return MusterBuilder
            .BuildFormations(force, soldier, _musterPlan, MusterTransferContext(force))
            .Select(ToFormationRow)
            .ToList();
    }

    internal MusterPreviewView QueryMusterPreview(int? soldierId, string formationSelectionKey)
    {
        PlayerForce force = _sector.PlayerForce;
        PlayerSoldier soldier = soldierId.HasValue ? FindMusterSoldier(soldierId.Value) : null;
        FormationVacancyViewModel formation = soldier == null
            ? null
            : FindFormation(force, soldier, formationSelectionKey);
        if (soldier == null || formation == null)
        {
            return new MusterPreviewView(
                "Select a candidate and formation",
                "The preview will make the resulting role explicit.",
                null,
                false,
                "Select a candidate and a legal destination before staging a change.");
        }

        if (formation.Option == null)
        {
            return new MusterPreviewView(
                $"{soldier.AssignedSquad.Name}  →  {formation.FormationName}",
                "This formation is already staged in the plan.\nUse the staged change card to edit it.",
                "FORMATION ALREADY STAGED",
                false,
                "This provisional formation is already included in the plan.");
        }

        bool soldierAlreadyStaged = _musterPlan.IsStaged(soldier.Id);
        return new MusterPreviewView(
            $"{soldier.AssignedSquad.Name}  →  {formation.FormationName}",
            $"{soldier.Template.Name}  →  {formation.ResultingRole}\n{formation.Location}",
            MutationButtonText(soldier, formation.Option),
            !soldierAlreadyStaged && !formation.IsFull,
            soldierAlreadyStaged
                ? "This soldier already has a staged change. Edit or undo that change before staging another."
                : formation.IsFull
                    ? "This formation reaches capacity after the staged changes and cannot accept another soldier."
                    : $"Add this {MutationDescription(soldier, formation.Option)} to the plan without committing it yet.");
    }

    internal MusterPlanView QueryMusterPlan()
    {
        PlayerForce force = _sector.PlayerForce;
        IReadOnlyList<MusterStagedAction> actions = _musterPlan.Actions;
        List<MusterPlanRow> rows = actions
            .Select((action, index) => new MusterPlanRow(
                action.ActionId,
                action.SoldierId,
                ActionIconKey(action.Kind),
                ActionIconTooltip(action.Kind),
                $"{index + 1}. {force?.Army?.PlayerSoldierMap.GetValueOrDefault(action.SoldierId)?.Name}"
                    + $"\n{action.SourceDisplay} → {action.TargetDisplay}"))
            .ToList();

        if (actions.Count == 0)
        {
            return new MusterPlanView(
                rows,
                MusterPlanState.Empty,
                "PLAN EMPTY",
                "No changes are staged. Add at least one personnel action to begin a Muster plan.",
                "Stage a personnel action to evaluate logistics.",
                false,
                "Stage at least one change before review.",
                string.Empty);
        }

        MusterPlanValidation validation = _musterPlan.Validate(force, MusterTransferContext(force));
        string reviewText = string.Join("\n\n", actions.Select((action, index) =>
            $"{index + 1}. {force?.Army?.PlayerSoldierMap.GetValueOrDefault(action.SoldierId)?.Name}"
                + $"\n{action.SourceDisplay}\n→ {action.TargetDisplay}"));

        if (validation.IsValid)
        {
            return new MusterPlanView(
                rows,
                MusterPlanState.Valid,
                $"VALID · {actions.Count} CHANGE(S)",
                "Every staged change is currently legal and all transport requirements are resolved.",
                "No relocation required, or all selected destinations have legal capacity.",
                true,
                "Review the complete transaction before committing any changes.",
                reviewText);
        }

        return new MusterPlanView(
            rows,
            MusterPlanState.Blocked,
            $"BLOCKED · {validation.Blockers.Count} ISSUE(S)",
            string.Join("\n", validation.Blockers),
            string.Join("\n", validation.Blockers.Select(blocker => "• " + blocker)),
            false,
            string.Join("\n", validation.Blockers),
            reviewText);
    }

    internal string DescribeStagedAction(int soldierId)
    {
        MusterStagedAction action = _musterPlan.Actions
            .FirstOrDefault(entry => entry.SoldierId == soldierId);
        return action == null
            ? string.Empty
            : $"STAGED: {action.SourceDisplay} → {action.TargetDisplay}";
    }

    internal Guid? StageMusterAction(Guid sessionToken, int soldierId, string formationSelectionKey)
    {
        if (_sector == null) return null;
        PlayerForce force = _sector.PlayerForce;
        PlayerSoldier soldier = FindMusterSoldier(soldierId);
        FormationVacancyViewModel formation = soldier == null
            ? null
            : FindFormation(force, soldier, formationSelectionKey);
        if (soldier == null || formation?.Option == null || formation.IsFull
            || _musterPlan.IsStaged(soldier.Id))
        {
            return null;
        }

        return _musterPlan.Stage(soldier, formation.Option).ActionId;
    }

    internal bool UndoMusterAction(Guid sessionToken, Guid actionId) =>
        _sector != null && _musterPlan.Undo(actionId);

    internal bool UndoLastMusterAction(Guid sessionToken)
    {
        if (_sector == null
            || _musterPlan.Actions.Count == 0)
        {
            return false;
        }

        return _musterPlan.Undo(_musterPlan.Actions[^1].ActionId);
    }

    internal void ClearMusterPlan(Guid sessionToken)
    {
        if (_sector == null) return;
        _musterPlan.Clear();
    }

    internal MusterCommitResultView CommitMusterPlan(Guid sessionToken)
    {
        if (_sector == null)
        {
            return new MusterCommitResultView(false, StaleSessionMessage);
        }

        MusterCommitResult result = _musterPlan.Commit(
            _sector.PlayerForce,
            _currentDate,
            _identity);
        return new MusterCommitResultView(
            result.Succeeded,
            result.Succeeded ? null : string.Join("\n", result.Errors));
    }

    private static IEnumerable<PlayerSoldier> MusterScope(PlayerForce force, int? scopeCompanyId) =>
        scopeCompanyId.HasValue
            ? force.Army.OrderOfBattle.ChildUnits
                .FirstOrDefault(unit => unit.Id == scopeCompanyId.Value)
                ?.GetAllMembers().OfType<PlayerSoldier>() ?? []
            : force.Army.PlayerSoldierMap.Values;

    private static SoldierTransferContext MusterTransferContext(PlayerForce force) =>
        force?.Army?.OrderOfBattle == null
            ? null
            : SoldierTransferContext.Build(force.Army.OrderOfBattle);

    private PlayerSoldier FindMusterSoldier(int soldierId) =>
        _sector.PlayerForce?.Army?.PlayerSoldierMap.GetValueOrDefault(soldierId);

    private Unit TryGetChapter() => _sector.PlayerForce?.Army?.OrderOfBattle;

    private FormationVacancyViewModel FindFormation(
        PlayerForce force, PlayerSoldier soldier, string selectionKey)
    {
        if (string.IsNullOrEmpty(selectionKey)) return null;
        return MusterBuilder
            .BuildFormations(force, soldier, _musterPlan, MusterTransferContext(force))
            .FirstOrDefault(row => FormationSelectionMatches(selectionKey, row));
    }

    private static MusterFormationRow ToFormationRow(FormationVacancyViewModel row) => new(
        row.Group,
        row.GroupLabel,
        row.FormationName,
        row.StateLabel,
        row.TypeLabel,
        row.SquadIconKey,
        row.RosterText,
        row.RosterTooltip,
        row.Location,
        row.ResultingRole,
        FormationSelectionKey(row),
        FormationTargetKey(row),
        row.IsFull,
        row.Option == null,
        row.CommonRow);

    private static string FormationSelectionKey(FormationVacancyViewModel formation)
    {
        if (formation == null) return null;
        if (!string.IsNullOrEmpty(formation.SelectionKey)) return formation.SelectionKey;
        if (formation.Option == null) return null;

        SoldierTransferOption option = formation.Option;
        return option.IsNewSquad
            ? $"new:{option.TargetUnit?.Id ?? int.MinValue}:{option.TargetSquadTemplate?.Id ?? int.MinValue}:{option.SoldierTemplate?.Id ?? int.MinValue}"
            : $"squad:{option.SquadId}:{option.SoldierTemplate?.Id ?? int.MinValue}";
    }

    private static string FormationTargetKey(FormationVacancyViewModel formation)
    {
        if (formation == null) return null;
        if (!string.IsNullOrEmpty(formation.SelectionKey)) return formation.SelectionKey;
        if (formation.Option == null) return null;

        SoldierTransferOption option = formation.Option;
        if (option.IsProvisionalSquad) return $"staged:{option.ProvisionalFormationId}";
        if (option.IsNewSquad)
        {
            return $"new:{option.TargetUnit?.Id ?? int.MinValue}:{option.TargetSquadTemplate?.Id ?? int.MinValue}";
        }
        return $"squad:{option.SquadId}";
    }

    private static bool FormationSelectionMatches(
        string selectionKey, FormationVacancyViewModel formation)
    {
        if (selectionKey.StartsWith("target:", StringComparison.Ordinal))
        {
            return string.Equals(
                selectionKey["target:".Length..],
                FormationTargetKey(formation),
                StringComparison.Ordinal);
        }
        return string.Equals(
            selectionKey, FormationSelectionKey(formation), StringComparison.Ordinal);
    }

    private static string MutationButtonText(PlayerSoldier soldier, SoldierTransferOption option)
    {
        if (option.IsNewSquad) return "STAGE PROMOTION & CREATE FORMATION";
        if (MusterPlanService.IsPromotion(soldier, option))
        {
            return "STAGE PROMOTION & ASSIGNMENT";
        }
        return option.SoldierTemplate == soldier.Template
            ? "STAGE TRANSFER"
            : "STAGE TRANSFER & ROLE CHANGE";
    }

    private static string MutationDescription(PlayerSoldier soldier, SoldierTransferOption option)
    {
        if (option.IsNewSquad) return "promotion and new formation";
        if (MusterPlanService.IsPromotion(soldier, option))
        {
            return "promotion and assignment";
        }
        return option.SoldierTemplate == soldier.Template
            ? "transfer"
            : "transfer and role change";
    }

    private static string ActionIconKey(MusterMutationKind kind) => kind switch
    {
        MusterMutationKind.PromotionAndCreateFormation => "formation_create",
        MusterMutationKind.FleetRebalance => "fleet_rebalance",
        _ => "route"
    };

    private static string ActionIconTooltip(MusterMutationKind kind) => kind switch
    {
        MusterMutationKind.PromotionAndCreateFormation => "Create new formation",
        MusterMutationKind.FleetRebalance => "Fleet rebalance",
        _ => "Personnel reassignment"
    };
}
