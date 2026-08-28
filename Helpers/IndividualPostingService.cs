using OnlyWar.Helpers.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Linq;

namespace OnlyWar.Helpers
{
    /// <summary>Owns posting, order-projection, and individual-ship-manifest invariants.</summary>
    public sealed class IndividualPostingService
    {
        public bool CanCreate(
            PlayerSoldier soldier,
            IndividualPostingKind kind,
            CampaignLocation location,
            Order order,
            out string reason)
        {
            reason = null;
            if (soldier?.AssignedSquad == null)
            {
                reason = "The soldier has no organizational home.";
                return false;
            }
            if (location == null || location.IsShip == location.IsRegion)
            {
                reason = "Select exactly one ship or region.";
                return false;
            }
            if (kind == IndividualPostingKind.OperationalAttachment && order == null)
            {
                reason = "An operational attachment requires an order.";
                return false;
            }
            if (kind != IndividualPostingKind.OperationalAttachment && order != null)
            {
                reason = "Only operational attachments can target an order.";
                return false;
            }
            if (kind != IndividualPostingKind.MedicalDetachment
                && kind != IndividualPostingKind.AwaitingReunion
                && soldier.AssignedSquad.SquadTemplate?.PermitsIndividualDetachment != true)
            {
                reason = "This formation does not permit individual detachment.";
                return false;
            }
            if (kind == IndividualPostingKind.OperationalAttachment
                && (!soldier.IsCombatEffective || Orders.OrderAttachment.IsReservedForProcedure(soldier)))
            {
                reason = "The specialist is not fit and available for operational duty.";
                return false;
            }
            if (location.Ship?.Fleet?.TravelPhase == Models.Fleets.FleetTravelPhase.InWarp)
            {
                reason = "Individuals cannot be posted through the Warp.";
                return false;
            }
            if (location.Ship != null)
            {
                int capacityAfterDeparture = location.Ship.AvailableCapacity;
                if (soldier.IndividualPosting == null
                    && ReferenceEquals(soldier.AssignedSquad?.BoardedLocation, location.Ship))
                {
                    capacityAfterDeparture++;
                }
                if (soldier.IndividualPosting?.Location?.IsSamePlace(location) != true
                    && capacityAfterDeparture < 1)
                {
                    reason = $"{location.Ship.Name} has no passenger berth available.";
                    return false;
                }
            }
            return true;
        }

        public IndividualPosting Create(
            PlayerSoldier soldier,
            IndividualPostingKind kind,
            CampaignLocation location,
            Date startedDate,
            Order order = null)
        {
            if (!CanCreate(soldier, kind, location, order, out string reason))
            {
                throw new InvalidOperationException(reason);
            }
            RemoveProjection(soldier);
            soldier.IndividualPosting = new IndividualPosting(
                kind,
                location,
                CloneDate(startedDate),
                order);
            AddProjection(soldier);
            CleanupEmptyPhysicalFormation(soldier.AssignedSquad);
            return soldier.IndividualPosting;
        }

        public IndividualPosting Restore(
            PlayerSoldier soldier,
            IndividualPostingKind kind,
            CampaignLocation location,
            Date startedDate,
            Order order = null)
        {
            if (soldier?.AssignedSquad == null)
                throw new InvalidOperationException("The posting soldier has no organizational home.");
            if (location == null || location.IsShip == location.IsRegion)
                throw new InvalidOperationException("The posting has an invalid location.");
            if (kind == IndividualPostingKind.OperationalAttachment && order == null)
                throw new InvalidOperationException("The operational posting has no order.");
            if (kind != IndividualPostingKind.OperationalAttachment && order != null)
                throw new InvalidOperationException("A non-operational posting targets an order.");
            if (location.Ship != null)
            {
                int capacityAfterDeparture = location.Ship.AvailableCapacity;
                if (soldier.IndividualPosting == null
                    && ReferenceEquals(soldier.AssignedSquad?.BoardedLocation, location.Ship))
                    capacityAfterDeparture++;
                if (soldier.IndividualPosting?.Location?.IsSamePlace(location) != true
                    && capacityAfterDeparture < 1)
                    throw new InvalidOperationException($"{location.Ship.Name} has no passenger berth available.");
            }
            RemoveProjection(soldier);
            soldier.IndividualPosting = new IndividualPosting(
                kind, location, CloneDate(startedDate), order);
            AddProjection(soldier);
            CleanupEmptyPhysicalFormation(soldier.AssignedSquad);
            return soldier.IndividualPosting;
        }

        public void Move(PlayerSoldier soldier, CampaignLocation location)
        {
            if (soldier?.IndividualPosting == null) throw new InvalidOperationException("Soldier is not posted.");
            if (location == null || location.IsShip == location.IsRegion)
            {
                throw new InvalidOperationException("Select exactly one ship or region.");
            }
            if (location.Ship?.Fleet?.TravelPhase == Models.Fleets.FleetTravelPhase.InWarp)
            {
                throw new InvalidOperationException("Individuals cannot move through the Warp.");
            }
            if (location.Ship != null
                && soldier.IndividualPosting.Location?.IsSamePlace(location) != true
                && location.Ship.AvailableCapacity < 1)
            {
                throw new InvalidOperationException($"{location.Ship.Name} has no passenger berth available.");
            }
            soldier.IndividualPosting.Location?.Ship?.DisembarkIndividual(soldier);
            soldier.IndividualPosting.Location = location;
            location.Ship?.BoardIndividual(soldier);
        }

        public void AttachToOrder(PlayerSoldier soldier, Order order, Date startedDate = null)
        {
            if (soldier == null || order == null) return;
            CampaignLocation destination = CampaignLocation.Landed(order.Mission?.RegionFaction?.Region);
            if (destination == null) throw new InvalidOperationException("Order has no physical region.");
            Create(soldier, IndividualPostingKind.OperationalAttachment, destination,
                startedDate ?? GameDataSingleton.Instance?.Date ?? new Date(1), order);
        }

        public void ReleaseFromOrder(PlayerSoldier soldier)
        {
            if (soldier?.IndividualPosting?.Kind != IndividualPostingKind.OperationalAttachment) return;
            RemoveProjection(soldier);
            soldier.IndividualPosting.Kind = IndividualPostingKind.IndependentDeployment;
            soldier.IndividualPosting.Order = null;
        }

        public void BeginMedicalDetachment(PlayerSoldier soldier, CampaignLocation location, Date date) =>
            Create(soldier, IndividualPostingKind.MedicalDetachment, location, date);

        public void MarkAwaitingReunion(PlayerSoldier soldier)
        {
            if (soldier?.IndividualPosting?.Kind == IndividualPostingKind.MedicalDetachment)
            {
                soldier.IndividualPosting.Kind = IndividualPostingKind.AwaitingReunion;
            }
        }

        public bool CanRejoin(PlayerSoldier soldier, out string reason)
        {
            reason = null;
            if (soldier?.IndividualPosting == null)
            {
                reason = "The soldier is not posted away from his formation.";
                return false;
            }
            if (!CampaignLocationService.AreCoLocated(soldier, soldier.AssignedSquad))
            {
                reason = "The soldier and home formation are not co-located.";
                return false;
            }
            return true;
        }

        public void Rejoin(PlayerSoldier soldier)
        {
            if (!CanRejoin(soldier, out string reason)) throw new InvalidOperationException(reason);
            RemoveOnDeath(soldier);
        }

        public void RemoveOnDeath(PlayerSoldier soldier)
        {
            if (soldier == null) return;
            RemoveProjection(soldier);
            soldier.IndividualPosting = null;
        }

        private static void AddProjection(PlayerSoldier soldier)
        {
            IndividualPosting posting = soldier.IndividualPosting;
            if (posting?.Order != null && !posting.Order.AttachedSoldiers.Contains(soldier))
            {
                posting.Order.AttachedSoldiers.Add(soldier);
            }
            posting?.Location?.Ship?.BoardIndividual(soldier);
        }

        private static void RemoveProjection(PlayerSoldier soldier)
        {
            soldier?.IndividualPosting?.Order?.AttachedSoldiers.Remove(soldier);
            soldier?.IndividualPosting?.Location?.Ship?.DisembarkIndividual(soldier);
        }

        private static Date CloneDate(Date date) => date == null
            ? new Date(1)
            : new Date(date.Millenium, date.Year, date.Week);

        private static void CleanupEmptyPhysicalFormation(Squad squad)
        {
            if (squad == null || SoldierPresenceService.PresentCount(squad) != 0) return;
            if (squad.CurrentOrders != null)
            {
                Orders.OrderAssignment.UnassignSquads([squad]);
            }
            squad.BoardedLocation?.RemoveSquad(squad);
            squad.BoardedLocation = null;
            if (squad.Faction != null
                && squad.CurrentRegion?.RegionFactionMap.TryGetValue(squad.Faction.Id, out var faction) == true)
            {
                faction.LandedSquads.Remove(squad);
            }
            squad.CurrentRegion = null;
        }
    }
}
