using OnlyWar.Operations.Abstractions;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;

namespace OnlyWar.Helpers
{
    /// <summary>
    /// Owns physical posting and individual-ship-manifest invariants. Operational order membership
    /// belongs to Operations and is retired through <see cref="IOrderCommitmentSurface"/>. A
    /// posting records only physical purpose and location; order membership stays on the soldier.
    ///
    /// The commitment surface is injected rather than called directly (SB-05b-1). Posting is a
    /// Campaign personnel concern and Campaign may not reference Operations, so an unassigning
    /// formation that no longer physically exists goes through the contract instead of the order
    /// services.
    /// </summary>
    public sealed class IndividualPostingService
    {
        private readonly IOrderCommitmentSurface _commitments;

        public IndividualPostingService(IOrderCommitmentSurface commitments)
        {
            _commitments = commitments
                ?? throw new ArgumentNullException(nameof(commitments));
        }

        public bool CanCreate(
            PlayerSoldier soldier,
            IndividualPostingPurpose purpose,
            CampaignLocation location,
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
            if (purpose == IndividualPostingPurpose.Independent
                && soldier.AssignedSquad?.PermitsIndividualDeployment != true)
            {
                reason = "This formation does not permit individual detachment.";
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
            IndividualPostingPurpose purpose,
            CampaignLocation location,
            Date startedDate)
        {
            if (!CanCreate(soldier, purpose, location, out string reason))
            {
                throw new InvalidOperationException(reason);
            }
            RemoveProjection(soldier);
            soldier.IndividualPosting = new IndividualPosting(
                purpose,
                location,
                CloneDate(startedDate));
            AddProjection(soldier);
            CleanupEmptyPhysicalFormation(soldier.AssignedSquad);
            return soldier.IndividualPosting;
        }

        /// <summary>
        /// Restores or creates the format-14 physical posting without changing an existing
        /// operational order relationship. This is the load/movement path for a character whose
        /// order and physical location are intentionally orthogonal.
        /// </summary>
        public IndividualPosting RestorePhysical(
            PlayerSoldier soldier,
            IndividualPostingPurpose purpose,
            CampaignLocation location,
            Date startedDate)
        {
            if (soldier?.AssignedSquad == null)
                throw new InvalidOperationException("The posting soldier has no organizational home.");
            if (location == null || location.IsShip == location.IsRegion)
                throw new InvalidOperationException("The posting has an invalid location.");
            if (location.Ship?.Fleet?.TravelPhase == Models.Fleets.FleetTravelPhase.InWarp)
                throw new InvalidOperationException("Individuals cannot be posted through the Warp.");
            if (location.Ship != null
                && soldier.IndividualPosting?.Location?.IsSamePlace(location) != true
                && location.Ship.AvailableCapacity < 1)
            {
                throw new InvalidOperationException($"{location.Ship.Name} has no passenger berth available.");
            }

            soldier.IndividualPosting?.Location?.Ship?.DisembarkIndividual(soldier);
            soldier.IndividualPosting = new IndividualPosting(
                purpose, location, CloneDate(startedDate));
            // CurrentOrder is deliberately preserved. A posting is physical state only.
            location.Ship?.BoardIndividual(soldier);
            CleanupEmptyPhysicalFormation(soldier.AssignedSquad);
            NormalizeReunion(soldier);
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

        public void BeginMedicalDetachment(PlayerSoldier soldier, CampaignLocation location, Date date) =>
            Create(soldier, IndividualPostingPurpose.Medical, location, date);

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
            soldier.CurrentOrder = null;
        }

        public void NormalizeReunion(PlayerSoldier soldier)
        {
            if (soldier?.IndividualPosting == null
                || soldier.CurrentOrder != null
                || soldier.IndividualPosting.Purpose != IndividualPostingPurpose.Independent
                || !CampaignLocationService.AreCoLocated(soldier, soldier.AssignedSquad))
            {
                return;
            }
            RemoveOnDeath(soldier);
        }

        private static void AddProjection(PlayerSoldier soldier)
        {
            if (soldier?.CurrentOrder != null
                && !soldier.CurrentOrder.AssignedCharacters.Contains(soldier))
            {
                soldier.CurrentOrder.AssignedCharacters.Add(soldier);
            }
            soldier?.IndividualPosting?.Location?.Ship?.BoardIndividual(soldier);
        }

        private void RemoveProjection(PlayerSoldier soldier)
        {
            if (soldier?.CurrentOrder != null)
            {
                _commitments.ReleaseCharacter(soldier);
            }
            soldier?.IndividualPosting?.Location?.Ship?.DisembarkIndividual(soldier);
        }

        private static Date CloneDate(Date date) => date == null
            ? new Date(1)
            : new Date(date.Millenium, date.Year, date.Week);

        private void CleanupEmptyPhysicalFormation(Squad squad)
        {
            if (squad == null || SoldierPresenceService.PresentCount(squad) != 0) return;
            // A seated administrative formation retains its organizational identity and duty
            // station even when every member is posted elsewhere. Member-only formations also
            // retain their last location so released members remain discoverable.
            if (squad.PermitsIndividualDeployment)
            {
                return;
            }
            if (squad.CurrentOrders != null)
            {
                _commitments.ReleaseSquad(squad);
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
