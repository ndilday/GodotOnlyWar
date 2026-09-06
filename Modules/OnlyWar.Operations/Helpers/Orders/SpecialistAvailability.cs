using OnlyWar.Helpers.Readiness;
using OnlyWar.Contracts.Operations;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Helpers.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Orders
{
    // The rules behind the Planetary Operations "ATTACHMENTS" roster group, extracted out of
    // PlanetaryOperationsScreenController for the same reason OrderAssignment was: a Godot partial class
    // cannot be unit-tested, so the decisions live here and the controller only does tree
    // wiring. See Design/Reference/SpecialistAttachment.md §7.1.
    public sealed class SpecialistOption
    {
        public PlayerSoldier Soldier { get; }
        public Squad HomeSquad { get; }
        // Where he currently is, for the roster's right-hand status column: the region of the
        // operation he is attached to, or "None".
        public string StatusLabel { get; }
        // Whether this person can be added to the order represented by the current roster
        // context. This is separate from row visibility/selection: like squads, an attached
        // specialist remains a selectable roster row so he can be recalled.
        public bool IsAvailable { get; }
        public PersonnelAvailabilityReasonCode ReasonCode { get; }
        public string Reason { get; }
        public bool IsSelectable => true;

        public SpecialistOption(
            PlayerSoldier soldier,
            Squad homeSquad,
            string statusLabel,
            bool isAvailable = true,
            PersonnelAvailabilityReasonCode reasonCode = PersonnelAvailabilityReasonCode.None,
            string reason = null)
        {
            Soldier = soldier;
            HomeSquad = homeSquad;
            StatusLabel = statusLabel;
            IsAvailable = isAvailable;
            ReasonCode = reasonCode;
            Reason = reason;
        }

        public string Label => $"{Soldier.Name} | {Soldier.Template.Name} | {HomeSquad?.Name}";
    }

    public static class SpecialistAvailability
    {
        /// <summary>
        /// Whether a formation belongs in the Orders squad roster. HQs and administrative
        /// formations are command/personnel pools rather than mission squads; formations that
        /// lend individual specialists are likewise never assigned as a whole squad.
        /// This is a type/role check only: empty or otherwise unavailable formations can still
        /// be shown by the Orders UI with an exclusion reason.
        /// </summary>
        public static bool IsMissionSquadFormation(Squad squad)
        {
            if (squad == null || !squad.CanAcceptSquadOrder
                || squad.SquadTemplate?.PermitsIndividualDetachment == true) return false;

            SquadTypes type = squad.SquadTemplate?.SquadType ?? SquadTypes.None;
            return (type & (SquadTypes.HQ
                | SquadTypes.Administrative)) == 0;
        }

        /// <summary>
        /// May this squad be offered in the Region Ops squad roster as a deployable unit?
        /// Formations that lend individuals are personnel pools and never deploy as units
        /// (§3.3), so they drop out of the squad list even though they stay operational.
        /// </summary>
        public static bool IsDeployableFormation(Squad squad)
        {
            return squad != null
                && squad.CanMoveAsFormation
                && squad.Members.Count > 0
                && !squad.PermitsIndividualDeployment
                && !squad.SquadTemplate.PermitsIndividualDetachment;
        }

        /// <summary>
        /// The individuals who may be lent to an operation staged out of <paramref name="originRegion"/>.
        /// Drawn from the landed squads of the player's presence there whose templates permit
        /// detachment, filtered through <see cref="OrderAttachment.CanAttach"/>.
        /// </summary>
        /// <param name="contextOrder">
        /// The order being edited, when the player re-opened an existing one from the inbound
        /// dossier -- its own attached specialists stay selectable. Null when issuing a fresh
        /// order, in which case anyone already committed elsewhere is excluded.
        /// </param>
        public static IReadOnlyList<SpecialistOption> EnumerateCandidates(
            RegionFaction playerRegionFaction,
            Region originRegion,
            IEnumerable<PlayerSoldier> rosterCharacters,
            IReadinessDecisions readiness,
            Order contextOrder = null,
            IOperationsPersonnelSurface personnel = null)
        {
            return EnumerateRoster(
                    playerRegionFaction,
                    originRegion,
                    rosterCharacters,
                    readiness,
                    contextOrder,
                    personnel)
                .Where(option => option.IsAvailable)
                .ToList();
        }

        /// <summary>
        /// Every member of a landed personnel-pool formation for the Region Ops roster. Unlike
        /// <see cref="EnumerateCandidates"/>, this keeps unavailable or already-attached men in
        /// the tree as selectable rows, so assigning a specialist never makes him disappear from
        /// the region detail view. Their assignment status is displayed beside the row, and the
        /// availability flag still prevents a second attachment from being accepted.
        /// </summary>
        /// <param name="rosterCharacters">
        /// The chapter roster the caller is looking at — every man who could be lent to an operation,
        /// wherever he currently is. Supplied explicitly rather than read from the active campaign
        /// (SB-05a); passing none restricts the roster to the region's own landed pools.
        /// </param>
        public static IReadOnlyList<SpecialistOption> EnumerateRoster(
            RegionFaction playerRegionFaction,
            Region originRegion,
            IEnumerable<PlayerSoldier> rosterCharacters,
            IReadinessDecisions readiness,
            Order contextOrder = null,
            IOperationsPersonnelSurface personnel = null)
        {
            if (playerRegionFaction == null)
            {
                return [];
            }

            IEnumerable<PlayerSoldier> globalCharacters =
                rosterCharacters ?? Enumerable.Empty<PlayerSoldier>();
            IOperationsPersonnelSurface surface =
                personnel ?? OperationsPersonnelDefaults.Current;
            IEnumerable<PlayerSoldier> localCharacters = playerRegionFaction.LandedSquads
                .Where(squad => squad?.PermitsIndividualDeployment == true
                    || squad?.SquadTemplate?.PermitsIndividualDetachment == true)
                .SelectMany(squad => squad.Members.OfType<PlayerSoldier>());
            return globalCharacters
                .Concat(localCharacters)
                .GroupBy(soldier => soldier.Id)
                .Select(group => group.First())
                .Where(soldier => soldier.AssignedSquad?.PermitsIndividualDeployment == true
                    || soldier.AssignedSquad?.SquadTemplate?.PermitsIndividualDetachment == true)
                .Select(soldier =>
                    {
                        bool assignedToContext = contextOrder != null
                            && ReferenceEquals(soldier.CurrentOrder, contextOrder);
                        SpecialistAvailabilityEvaluation evaluation = assignedToContext
                            ? SpecialistAvailabilityEvaluation.Allowed
                            : soldier.AssignedSquad?.PermitsIndividualDeployment == true
                                ? Project(surface.EvaluateOrderAssignment(
                                    soldier,
                                    contextOrder,
                                    originRegion,
                                    contextOrder?.AssignedSquads))
                                : EvaluateLegacyCandidate(
                                    soldier, contextOrder, originRegion, readiness);
                        return new SpecialistOption(
                            soldier,
                            soldier.AssignedSquad,
                            DescribeStatus(soldier, evaluation),
                            evaluation.IsAllowed,
                            evaluation.ReasonCode,
                            evaluation.Reason);
                    })
                .OrderBy(option => option.HomeSquad?.Name)
                .ThenBy(option => option.Soldier.Name)
                .ToList();
        }

        private static SpecialistAvailabilityEvaluation Project(
            PersonnelAvailabilityDecision decision) =>
            decision == null
                ? new SpecialistAvailabilityEvaluation(
                    false,
                    PersonnelAvailabilityReasonCode.NotCombatEffective,
                    "No availability decision was returned.")
                : new SpecialistAvailabilityEvaluation(
                    decision.IsAllowed,
                    (PersonnelAvailabilityReasonCode)decision.ReasonCode,
                    decision.Reason);

        private static SpecialistAvailabilityEvaluation EvaluateLegacyCandidate(
            PlayerSoldier soldier,
            Order contextOrder,
            Region originRegion,
            IReadinessDecisions readiness)
        {
            bool isAvailable = OrderAttachment.CanAttach(
                soldier, contextOrder, null, originRegion, readiness, out string reason);
            return isAvailable
                ? SpecialistAvailabilityEvaluation.Allowed
                : new SpecialistAvailabilityEvaluation(
                    false,
                    PersonnelAvailabilityReasonCode.NotAtOrigin,
                    reason ?? "The character is not available at this origin.");
        }

        private static string DescribeStatus(
            PlayerSoldier soldier,
            SpecialistAvailabilityEvaluation evaluation)
        {
            if (soldier.CurrentOrder != null)
            {
                string regionName = soldier.CurrentOrder.Mission?.RegionFaction?.Region?.Name;
                return regionName ?? "None";
            }
            return evaluation.IsAllowed
                ? "None"
                : evaluation.Reason ?? "Unavailable";
        }

        /// <summary>
        /// Specialists already attached to a given order, for the "release both" half of the
        /// Unassign action and for re-selecting an order from the inbound dossier.
        /// </summary>
        public static IReadOnlyList<PlayerSoldier> AttachedTo(Order order)
        {
            return order?.AssignedCharacters.ToList() ?? [];
        }
    }

    sealed record SpecialistAvailabilityEvaluation(
        bool IsAllowed,
        PersonnelAvailabilityReasonCode ReasonCode,
        string Reason)
    {
        public static SpecialistAvailabilityEvaluation Allowed { get; } =
            new(true, PersonnelAvailabilityReasonCode.None, null);
    }
}
