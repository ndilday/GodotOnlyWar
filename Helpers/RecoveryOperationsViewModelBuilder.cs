using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public sealed class RecoveryOperationsViewModelBuilder
    {
        private readonly ApothecariumMedicalRecordBuilder _medical = new();
        private readonly CareDestinationService _destinations = new();

        public RecoveryOperationsViewModel Build(
            PlayerForce force,
            IEnumerable<Planet> planets,
            int? selectedSoldierId,
            RecoverySortMode sortMode,
            bool ascending,
            CampaignLocation selectedDestination = null,
            RecoveryMovementChoice movement = RecoveryMovementChoice.None,
            int? selectedHitLocationId = null,
            MedicalProcedureType? selectedProcedureType = null)
        {
            List<PlayerSoldier> patients = force?.Army?.PlayerSoldierMap?.Values
                .Where(IsInRecoveryQueue).ToList() ?? [];
            List<RecoveryQueueRow> queue = patients.Select(soldier => BuildQueueRow(force, soldier)).ToList();
            queue = Sort(queue, sortMode, ascending).ToList();
            PlayerSoldier patient = patients.FirstOrDefault(soldier => soldier.Id == selectedSoldierId)
                ?? patients.FirstOrDefault(soldier => soldier.Id == queue.FirstOrDefault()?.SoldierId);
            if (patient == null)
            {
                return new(queue, null, null, null, [], null, movement, [], 0, false, "No casualties require recovery action.");
            }

            MedicalSoldierSummary summary = _medical.BuildSoldierSummary(patient, force);
            if (patient.IndividualPosting?.Kind == IndividualPostingKind.AwaitingReunion)
            {
                bool canRejoin = CampaignLocationService.AreCoLocated(patient, patient.AssignedSquad);
                IReadOnlyList<RecoveryAction> reunionActions =
                [
                    new("rejoin", canRejoin ? "REJOIN NOW" : "PLAN REUNION MOVEMENT",
                        canRejoin
                            ? $"Rejoin {patient.AssignedSquad?.Name}."
                            : "Move the soldier or home squad until they are co-located.",
                        canRejoin ? RecoveryActionState.Pending : RecoveryActionState.Blocked)
                ];
                return new(queue, summary, BuildSquadStatus(patient), null, [], null,
                    RecoveryMovementChoice.None, reunionActions, 0, canRejoin,
                    canRejoin ? "Ready to reunite with the home formation." : "Reunion movement is required.");
            }
            ReplacementOption option = summary.ReplacementOptions.FirstOrDefault(candidate =>
                candidate.HitLocationId == selectedHitLocationId
                && candidate.Type == selectedProcedureType)
                ?? summary.ReplacementOptions.FirstOrDefault();
            IReadOnlyList<CareDestinationCandidate> candidates = option == null
                ? []
                : _destinations.Enumerate(force, planets, patient, option);
            CareDestinationCandidate selected = candidates.FirstOrDefault(candidate =>
                candidate.Location.IsSamePlace(selectedDestination));
            IReadOnlyList<RecoveryAction> actions = BuildActions(patient, option, selected, movement);
            bool canConfirm = option != null
                && selected?.State != CareDestinationState.Ineligible
                && movement != RecoveryMovementChoice.None
                && actions.All(action => action.State != RecoveryActionState.Blocked);
            return new(
                queue,
                summary,
                BuildSquadStatus(patient),
                option,
                candidates,
                selected,
                movement,
                actions,
                option?.RequisitionCost ?? 0,
                canConfirm,
                canConfirm ? "Ready to review and confirm." : "Choose a legal destination and movement plan.");
        }

        private RecoveryQueueRow BuildQueueRow(PlayerForce force, PlayerSoldier soldier)
        {
            MedicalSoldierSummary summary = _medical.BuildSoldierSummary(soldier, force);
            WoundLocationSummary worst = summary.Wounds
                .OrderByDescending(wound => wound.IsSevered)
                .ThenByDescending(wound => wound.PrincipalWoundLevel)
                .FirstOrDefault();
            string posting = soldier.IndividualPosting?.Kind switch
            {
                IndividualPostingKind.OperationalAttachment => "WITH ORDER",
                IndividualPostingKind.MedicalDetachment => "IN MEDICAL CARE",
                IndividualPostingKind.AwaitingReunion => "AWAITING REUNION",
                IndividualPostingKind.IndependentDeployment => "POSTED",
                _ => null
            };
            return new RecoveryQueueRow(
                soldier.Id,
                soldier.Name,
                summary.IconKey,
                $"{soldier.AssignedSquad?.Name ?? "Unassigned"} / {soldier.AssignedSquad?.ParentUnit?.Name ?? "No company"}",
                CampaignLocationService.Format(CampaignLocationService.ForSoldier(soldier)),
                worst?.IsSevered == true ? "LOST" : worst?.PrincipalWoundLevel.ToString().ToUpperInvariant() ?? "RECOVERY",
                summary.MaxRecoveryWeeks,
                worst?.PrincipalWoundLevel ?? WoundLevel.None,
                posting,
                BuildCareGaps(force, soldier, summary.ReplacementOptions));
        }

        private IReadOnlyList<string> BuildCareGaps(
            PlayerForce force,
            PlayerSoldier soldier,
            IEnumerable<ReplacementOption> options)
        {
            MedicalProcedureService procedures = new();
            return (options ?? [])
                .SelectMany(option => procedures.EvaluateRequisites(force, soldier, option))
                .Where(requisite => !requisite.IsMet)
                .Select(requisite => requisite.Label.StartsWith("Apothecary") ? "NO APOTHECARY"
                    : requisite.Label.StartsWith("Techmarine") ? "NO TECHMARINE"
                    : requisite.Label.StartsWith("Valid surgery") ? "NO LIMB FACILITY"
                    : requisite.Label.ToUpperInvariant())
                .Distinct()
                .ToList();
        }

        private static RecoverySquadStatus BuildSquadStatus(PlayerSoldier patient)
        {
            Squad squad = patient.AssignedSquad;
            SquadStrengthSnapshot strength = SquadStrengthSnapshotBuilder.Build(squad);
            return new RecoverySquadStatus(
                squad?.Name ?? "Unassigned",
                squad?.ParentUnit?.Name ?? "No company",
                $"{strength.DutyReady}/{strength.Full} duty-ready"
                    + (strength.Unavailable > 0 ? $" · {strength.Unavailable} unavailable" : string.Empty),
                CampaignLocationService.Format(CampaignLocationService.ForSquad(squad)),
                squad?.CurrentOrders?.Mission?.MissionType.ToString() ?? "No order");
        }

        private static IReadOnlyList<RecoveryAction> BuildActions(
            PlayerSoldier patient,
            ReplacementOption option,
            CareDestinationCandidate destination,
            RecoveryMovementChoice movement)
        {
            List<RecoveryAction> actions = [];
            if (movement == RecoveryMovementChoice.DetachCasualty)
            {
                actions.Add(new("detach", $"TEMPORARILY DETACH {patient.Name.ToUpperInvariant()}",
                    "Home squad retained; present and deployable strength fall by one.", RecoveryActionState.Pending));
            }
            else if (movement == RecoveryMovementChoice.MoveWholeSquad)
            {
                actions.Add(new("move-squad", $"MOVE {patient.AssignedSquad?.Name?.ToUpperInvariant()}",
                    "The whole formation moves through ordinary deployment rules.",
                    patient.AssignedSquad?.CanMoveAsFormation == true
                        ? RecoveryActionState.Pending
                        : RecoveryActionState.Blocked));
            }
            if (patient.CurrentOrder != null || patient.AssignedSquad?.CurrentOrders != null)
            {
                actions.Add(new("release", "RELEASE FROM ORDER", "The remainder of the squad continues when detached.", RecoveryActionState.Pending));
            }
            if (destination == null)
            {
                actions.Add(new("destination", "SELECT CARE DESTINATION", "No destination selected.", RecoveryActionState.Blocked));
            }
            else
            {
                actions.Add(new("move", "EMBARK / LAND PATIENT", CampaignLocationService.Format(destination.Location),
                    destination.State == CareDestinationState.Ineligible ? RecoveryActionState.Blocked : RecoveryActionState.Pending));
                foreach (CareDestinationReason reason in destination.Reasons)
                {
                    actions.Add(new(reason.Code, reason.Message, reason.IsResolvable ? "Will be staged" : "Cannot be resolved", reason.IsResolvable ? RecoveryActionState.Pending : RecoveryActionState.Blocked));
                }
            }
            if (option != null)
            {
                actions.Add(new("procedure", $"PERFORM {option.Title.ToUpperInvariant()}",
                    $"{option.Weeks} weeks / {option.RequisitionCost} requisition", RecoveryActionState.Pending));
            }
            return actions;
        }

        private static bool IsInRecoveryQueue(PlayerSoldier soldier) => soldier != null
            && (soldier.IsWounded
                || soldier.IsUndergoingMedicalProcedure
                || soldier.IndividualPosting?.Kind == IndividualPostingKind.MedicalDetachment
                || soldier.IndividualPosting?.Kind == IndividualPostingKind.AwaitingReunion);

        private static IEnumerable<RecoveryQueueRow> Sort(
            IEnumerable<RecoveryQueueRow> rows,
            RecoverySortMode mode,
            bool ascending)
        {
            Func<RecoveryQueueRow, object> key = mode switch
            {
                RecoverySortMode.RecoveryTime => row => row.RecoveryWeeks,
                RecoverySortMode.Squad => row => row.Home,
                RecoverySortMode.Location => row => row.Location,
                _ => row => row.WoundLevel
            };
            IOrderedEnumerable<RecoveryQueueRow> ordered = ascending
                ? rows.OrderBy(key)
                : rows.OrderByDescending(key);
            return ordered.ThenBy(row => row.Name);
        }
    }
}
