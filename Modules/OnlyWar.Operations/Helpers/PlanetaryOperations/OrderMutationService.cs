using OnlyWar.Operations.Abstractions;
using OnlyWar.Operations.Readiness;
using OnlyWar.Domain.Missions;
using OnlyWar.Operations.Orders;
using OnlyWar.Domain.Extensions;
using OnlyWar.Operations.Personnel;
using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Operations.Planetary
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

    /// <summary>Opaque, session-bound cancellation undo. Only the command owner can issue it.</summary>
    public sealed class OrderRestoreToken
    {
        internal Sector Sector { get; }
        internal Order Order { get; }
        internal IReadOnlyList<Squad> Squads { get; }
        internal IReadOnlyList<PlayerSoldier> Characters { get; }

        internal OrderRestoreToken(Sector sector, Order order)
        {
            Sector = sector;
            Order = order;
            Squads = order.AssignedSquads.ToArray();
            Characters = order.AssignedCharacters.ToArray();
        }
    }

    /// <summary>
    /// Validated, UI-facing order mutations. Squads already committed elsewhere are rejected
    /// instead of being silently detached and reassigned.
    /// </summary>
    public static class OrderMutationService
    {
        public static OrderRestoreToken CaptureCancellationUndo(Sector sector, Order order) =>
            sector != null && IsPlayerOrder(order) && sector.Orders.Values.Contains(order)
                ? new OrderRestoreToken(sector, order)
                : null;

        public static OrderMutationResult Restore(
            Sector sector,
            OrderRestoreToken token,
            IReadinessDecisions readiness,
            Date currentDate = null,
            IPersonnelAvailabilityQueries personnel = null) =>
            token != null && ReferenceEquals(sector, token.Sector)
                ? RestoreParticipants(
                    sector, token.Order, token.Squads, token.Characters, readiness,
                    currentDate, personnel)
                : Failure("The operation belongs to a campaign that is no longer active.");

        public static OrderMutationResult CreateOrAdd(
            Sector sector,
            Region target,
            AvailableMission mission,
            IReadOnlyList<Squad> selectedSquads,
            int targetFactionId,
            Aggression aggression,
            IReadinessDecisions readiness,
            Date currentDate = null,
            IPersonnelAvailabilityQueries personnel = null,
            IPersistentIdAllocator identity = null)
        {
            return CreateOrAdd(sector, target, mission, selectedSquads, [],
                targetFactionId, aggression, readiness, currentDate, personnel, identity);
        }

        // currentDate stamps any operational posting the issue creates. It is an explicit input so
        // the command resolves entirely against the sector it was given (SB-05a).
        public static OrderMutationResult CreateOrAdd(
            Sector sector,
            Region target,
            AvailableMission mission,
            IReadOnlyList<Squad> selectedSquads,
            IReadOnlyList<PlayerSoldier> selectedCharacters,
            int targetFactionId,
            Aggression aggression,
            IReadinessDecisions readiness,
            Date currentDate = null,
            IPersonnelAvailabilityQueries personnel = null,
            IPersistentIdAllocator identity = null)
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
                && readiness.EvaluateSquad(
                        squad,
                        program: ForceReadinessInputs.ProgramFor(sector.PlayerForce, squad),
                        doctrine: ForceReadinessInputs.DoctrineFor(sector.PlayerForce, squad))
                    .PrimaryBlocker == SquadReadinessBlocker.Leaderless))
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
                : RegionalOrderEligibilityService.Build(sector, target, readiness, mission, existing);
            HashSet<int> selectableIds = eligibility?.Candidates
                .Where(candidate => candidate.IsSelectable)
                .Select(candidate => candidate.Squad.Id)
                .ToHashSet() ?? [];
            if (squads.Any(squad => !selectableIds.Contains(squad.Id)))
            {
                return Failure("The force changed and at least one selected squad is no longer eligible.");
            }

            Order result = OrderAssignment.AssignParticipantsToMission(
                new OrderCommandContext(sector, currentDate, readiness, personnel, identity),
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
            PlayerSoldier soldier,
            IReadinessDecisions readiness,
            Date currentDate = null,
            IPersonnelAvailabilityQueries personnel = null)
        {
            if (sector == null || !IsPlayerOrder(order)
                || !sector.Orders.Values.Contains(order))
            {
                return Failure("That order is no longer active.");
            }
            IPersonnelAvailabilityQueries surface = personnel
                ?? throw new System.ArgumentNullException(nameof(personnel));
            PersonnelAvailabilityDecision availability = surface.EvaluateOrderAssignment(
                PersonnelAvailabilityProjection.ForOrderAssignment(
                    soldier,
                    order,
                    null,
                    order.AssignedSquads,
                    ForceReadinessInputs.DoctrineFor(sector.PlayerForce, soldier?.AssignedSquad),
                    ForceReadinessInputs.ProgramFor(sector.PlayerForce, soldier?.AssignedSquad)));
            if (!availability.IsAllowed)
            {
                return Failure(availability.Reason ?? "That character cannot join this order.");
            }
            if (!OrderForceService.AssignCharacter(order, soldier, readiness))
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
            Squad squad,
            IReadinessDecisions readiness,
            Date currentDate = null,
            IPersonnelAvailabilityQueries personnel = null)
            => RestoreParticipants(
                sector, order, [squad], [], readiness, currentDate, personnel);

        /// <summary>
        /// Restores a cancelled order as one command. Validate the entire participant set before
        /// registering the order or changing either side of a commitment relationship.
        /// </summary>
        public static OrderMutationResult RestoreParticipants(
            Sector sector,
            Order order,
            IReadOnlyList<Squad> selectedSquads,
            IReadOnlyList<PlayerSoldier> selectedCharacters,
            IReadinessDecisions readiness,
            Date currentDate = null,
            IPersonnelAvailabilityQueries personnel = null)
        {
            List<Squad> squads = (selectedSquads ?? []).ToList();
            List<PlayerSoldier> characters = (selectedCharacters ?? []).ToList();
            if (sector?.PlayerForce == null || order?.Mission == null || !IsPlayerOrder(order)
                || squads.Count + characters.Count == 0
                || squads.Any(squad => squad == null)
                || characters.Any(character => character == null)
                || squads.DistinctBy(squad => squad.Id).Count() != squads.Count
                || characters.DistinctBy(character => character.Id).Count() != characters.Count)
            {
                return Failure("The previous squad assignment can no longer be restored.");
            }
            Region target = order.Mission?.RegionFaction?.Region;
            if (target?.Planet == null
                || !sector.Planets.TryGetValue(target.Planet.Id, out Planet currentPlanet)
                || !ReferenceEquals(currentPlanet, target.Planet)
                || !currentPlanet.Regions.Contains(target)
                || sector.Orders.TryGetValue(order.Id, out Order registered)
                    && !ReferenceEquals(registered, order))
            {
                return Failure("The operation belongs to a campaign that is no longer active.");
            }
            ChapterOperationalDoctrine doctrine = sector.PlayerForce.Army?.ChapterOperationalDoctrine;
            var program = sector.PlayerForce.RecruitmentProgram;
            if (squads.Any(squad => squad.CurrentOrders != null
                || !ReferenceEquals(squad.Faction, sector.PlayerForce.Faction)
                || squad.CurrentRegion == null
                || !squad.CanAcceptSquadOrder
                || !readiness.CanBeginNewDeployment(squad, program, doctrine)))
            {
                return Failure("The force changed and a squad can no longer begin deployment.");
            }
            if (squads.Any(squad => !target.GetSelfAndAdjacentRegions().Contains(squad.CurrentRegion)))
            {
                return Failure("The squad is no longer in the operation's staging area.");
            }

            List<Squad> staging = order.AssignedSquads.Concat(squads).ToList();
            IPersonnelAvailabilityQueries surface = personnel
                ?? throw new System.ArgumentNullException(nameof(personnel));
            foreach (PlayerSoldier character in characters)
            {
                if (character.CurrentOrder != null
                    || !ReferenceEquals(character.AssignedSquad?.Faction, sector.PlayerForce.Faction)
                    || !readiness.EvaluateSoldier(character, doctrine, program).IsDutyReady)
                {
                    return Failure("The force changed and a character can no longer join the operation.");
                }
                PersonnelAvailabilityDecision availability = surface.EvaluateOrderAssignment(
                    PersonnelAvailabilityProjection.ForOrderAssignment(
                        character,
                        order,
                        null,
                        staging,
                        doctrine,
                        program));
                if (!availability.IsAllowed)
                {
                    return Failure(availability.Reason
                        ?? "The character can no longer join the operation.");
                }
            }

            // These are the same readiness and commitment guards used by the mutation owner.
            // No callbacks or simulation execute between validation and this synchronous commit.
            OrderForceService.CommitParticipantRestore(order, squads, characters);
            if (!sector.Orders.Values.Contains(order)) sector.AddNewOrder(order);
            return new OrderMutationResult(true, "Order assignments restored.",
                OrderMutationKind.Restored, order, squads.Count, characters.Count);
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
