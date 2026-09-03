using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Orders;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Helpers.UI;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.PlanetaryOperations
{
    public enum OrderMutationKind
    {
        None,
        Created,
        Reinforced,
        RemovedSquad,
        Cancelled,
        AggressionChanged,
        SpecialistAttached,
        SpecialistDetached,
        Restored
    }

    public sealed record OrderMutationResult(
        bool Succeeded,
        string Message,
        OrderMutationKind Kind = OrderMutationKind.None,
        Order Order = null,
        int AffectedSquads = 0,
        int ReleasedSpecialists = 0);

    /// <summary>
    /// Validated, UI-facing order mutations. Unlike OrderAssignment's legacy API, squads already
    /// committed elsewhere are rejected instead of being silently detached and reassigned.
    /// </summary>
    public static class OrderMutationService
    {
        public static OrderMutationResult CreateOrAdd(
            Sector sector,
            Region target,
            AvailableMission mission,
            IReadOnlyList<Squad> selectedSquads,
            int targetFactionId,
            Aggression aggression)
        {
            return CreateOrAdd(sector, target, mission, selectedSquads, [],
                targetFactionId, aggression);
        }

        public static OrderMutationResult CreateOrAdd(
            Sector sector,
            Region target,
            AvailableMission mission,
            IReadOnlyList<Squad> selectedSquads,
            IReadOnlyList<PlayerSoldier> selectedCharacters,
            int targetFactionId,
            Aggression aggression)
        {
            if (sector == null || target == null || mission == null)
            {
                return Failure("The target or mission is no longer available.");
            }

            List<Squad> squads = (selectedSquads ?? [])
                .Where(squad => squad != null)
                .DistinctBy(squad => squad.Id)
                .ToList();
            List<PlayerSoldier> characters = (selectedCharacters ?? [])
                .Where(character => character != null)
                .DistinctBy(character => character.Id)
                .ToList();
            if (squads.Count == 0 && characters.Count == 0)
            {
                return Failure("Select at least one eligible squad.");
            }
            if (squads.Any(squad => squad.CurrentOrders == null
                && SquadReadinessService.Evaluate(squad).PrimaryBlocker
                    == SquadReadinessBlocker.Leaderless))
            {
                return Failure("A formation that requires a leader cannot begin a new deployment.");
            }

            Order existing = FindEquivalentOrder(
                sector, target, mission, targetFactionId);
            if (!IsSpecialMissionCurrent(target, mission))
            {
                return Failure("That special mission is no longer available.");
            }
            if (squads.Count == 0 && existing == null)
            {
                return Failure("An operation requires at least one squad; characters can reinforce an existing order.");
            }
            if (squads.Any(squad => squad.CurrentOrders != null)
                || characters.Any(character => character.CurrentOrder != null
                    && !ReferenceEquals(character.CurrentOrder, existing)))
            {
                return Failure("A selected squad already has another order.");
            }

            RegionalEligibilityResult eligibility = squads.Count == 0
                ? null
                : RegionalOrderEligibilityService.Build(sector, target, mission, existing);
            HashSet<int> selectableIds = eligibility?.Candidates
                .Where(candidate => candidate.IsSelectable)
                .Select(candidate => candidate.Squad.Id)
                .ToHashSet() ?? [];
            if (squads.Any(squad => !selectableIds.Contains(squad.Id)))
            {
                return Failure("The force changed and at least one selected squad is no longer eligible.");
            }

            Order result = OrderAssignment.AssignParticipantsToMission(
                squads, characters, target, mission, targetFactionId, aggression);
            if (result == null)
            {
                return Failure("The order could not be issued; no campaign state changed.");
            }

            return new OrderMutationResult(
                true,
                existing == null ? "Order created." : "Order reinforced.",
                existing == null ? OrderMutationKind.Created : OrderMutationKind.Reinforced,
                result,
                squads.Count,
                characters.Count);
        }

        public static OrderMutationResult RemoveSquad(
            Sector sector,
            Order order,
            Squad squad)
        {
            if (!IsPlayerOrder(order)
                || squad == null
                || !ReferenceEquals(squad.CurrentOrders, order)
                || !order.AssignedSquads.Contains(squad))
            {
                return Failure("That squad no longer belongs to the selected order.");
            }

            int specialists = order.AssignedSquads.Count == 1
                ? order.AssignedCharacters.Count
                : 0;
            if (!OrderAssignment.UnassignSquads([squad]))
            {
                return Failure("The squad could not be removed.");
            }

            return new OrderMutationResult(
                true,
                order.Force.IsEmpty ? "Order ended." : "Squad removed.",
                OrderMutationKind.RemovedSquad,
                order,
                1,
                specialists);
        }

        public static OrderMutationResult Cancel(Sector sector, Order order)
        {
            if (sector == null || !IsPlayerOrder(order)
                || !sector.Orders.Values.Contains(order))
            {
                return Failure("That order is no longer active.");
            }

            List<Squad> squads = order.AssignedSquads.ToList();
            int specialists = order.AssignedCharacters.Count;
            if (squads.Count == 0 && specialists == 0)
            {
                return Failure("The order could not be cancelled.");
            }
            OrderForceService.ReleaseOrder(order);

            return new OrderMutationResult(
                true,
                "Order cancelled.",
                OrderMutationKind.Cancelled,
                order,
                squads.Count,
                specialists);
        }

        public static OrderMutationResult SetAggression(
            Sector sector,
            Order order,
            Aggression aggression)
        {
            if (sector == null || !IsPlayerOrder(order)
                || !sector.Orders.Values.Contains(order))
            {
                return Failure("That order is no longer active.");
            }
            if (!System.Enum.IsDefined(aggression))
            {
                return Failure("Select a valid aggression level.");
            }
            if (order.LevelOfAggression == aggression)
            {
                return new OrderMutationResult(true, "Aggression is unchanged.",
                    OrderMutationKind.AggressionChanged, order);
            }
            order.SetAggression(aggression);
            return new OrderMutationResult(true, $"Aggression set to {aggression}.",
                OrderMutationKind.AggressionChanged, order);
        }

        public static OrderMutationResult AttachSpecialist(
            Sector sector,
            Order order,
            PlayerSoldier soldier)
        {
            if (sector == null || !IsPlayerOrder(order)
                || !sector.Orders.Values.Contains(order))
            {
                return Failure("That order is no longer active.");
            }
            CharacterAvailabilityEvaluation availability =
                new OnlyWar.Helpers.CharacterAvailabilityService()
                    .EvaluateOrderAssignment(soldier, order, null, order.AssignedSquads);
            if (!availability.IsAllowed)
            {
                // Keep the format-13 attachment façade usable for old fixtures and saves whose
                // rules template still carries only PermitsIndividualDetachment. New campaign
                // data always takes the explicit MembersOnly capability path above.
                if (!OrderAttachment.CanAttach(
                        soldier, order, order.AssignedSquads, null, out string legacyReason))
                {
                    return Failure(availability.Reason ?? legacyReason
                        ?? "That character cannot join this order.");
                }
                OrderAttachment.Attach(soldier, order);
                return new OrderMutationResult(true, $"{soldier.Name} assigned.",
                    OrderMutationKind.SpecialistAttached, order, ReleasedSpecialists: 1);
            }
            if (!OrderForceService.AssignCharacter(order, soldier))
            {
                return Failure("That character could not join the order.");
            }
            return new OrderMutationResult(true, $"{soldier.Name} assigned.",
                OrderMutationKind.SpecialistAttached, order, ReleasedSpecialists: 1);
        }

        public static OrderMutationResult DetachSpecialist(
            Sector sector,
            Order order,
            PlayerSoldier soldier)
        {
            if (sector == null || !IsPlayerOrder(order)
                || soldier == null
                || !ReferenceEquals(soldier.CurrentOrder, order))
            {
                return Failure("That specialist is no longer attached to this order.");
            }
            OrderForceService.RemoveCharacter(order, soldier);
            return new OrderMutationResult(true, $"{soldier.Name} removed.",
                OrderMutationKind.SpecialistDetached, order, ReleasedSpecialists: 1);
        }

        public static OrderMutationResult RestoreSquad(
            Sector sector,
            Order order,
            Squad squad)
        {
            if (sector == null || order?.Mission == null
                || squad == null || squad.CurrentOrders != null
                || squad.Faction?.IsPlayerFaction != true
                || squad.CurrentRegion == null
                || !squad.CanAcceptSquadOrder)
            {
                return Failure("The previous squad assignment can no longer be restored.");
            }
            Region target = order.Mission?.RegionFaction?.Region;
            if (target == null || !target.GetSelfAndAdjacentRegions().Contains(squad.CurrentRegion))
            {
                return Failure("The squad is no longer in the operation's staging area.");
            }
            if (!sector.Orders.Values.Contains(order)) sector.AddNewOrder(order);
            OrderForceService.AssignSquad(order, squad);
            return new OrderMutationResult(true, "Squad assignment restored.",
                OrderMutationKind.Restored, order, 1);
        }

        public static Order FindEquivalentOrder(
            Sector sector,
            Region target,
            AvailableMission mission,
            int targetFactionId)
        {
            if (sector == null || target == null || mission == null) return null;
            return sector.Orders.Values.FirstOrDefault(order =>
                IsPlayerOrder(order)
                && ReferenceEquals(order.Mission?.RegionFaction?.Region, target)
                && RepresentsEffectiveMission(order, mission, targetFactionId));
        }

        private static bool IsPlayerOrder(Order order) =>
            order?.OwnerFaction?.IsPlayerFaction == true
            || (order?.OwnerFaction == null && order?.Force?.AllPlayerSoldiers?.Any() == true);

        private static bool IsSpecialMissionCurrent(
            Region target,
            AvailableMission mission) =>
            mission.Kind != MissionAvailabilityKind.Special
            || (mission.SpecialMission != null
                && target.SpecialMissions.Any(candidate =>
                    candidate.Id == mission.SpecialMission.Id));

        private static bool RepresentsEffectiveMission(
            Order order,
            AvailableMission mission,
            int targetFactionId)
        {
            if (!mission.RepresentsOrder(order)) return false;
            if (mission.Kind == MissionAvailabilityKind.Diversion)
            {
                return order.Mission?.MissionType == MissionType.Diversion
                    && order.Mission.RegionFaction?.PlanetFaction?.Faction?.Id
                        == targetFactionId;
            }
            return true;
        }

        private static OrderMutationResult Failure(string message) =>
            new(false, message);
    }
}
