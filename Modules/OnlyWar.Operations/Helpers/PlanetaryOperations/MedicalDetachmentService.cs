using OnlyWar.Operations.Abstractions;
using OnlyWar.Operations.Orders;
using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Soldiers;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Operations.Planetary
{
    public sealed record MedicalDetachmentResult(
        bool Succeeded,
        string Message,
        int DetachedCount = 0);

    /// <summary>
    /// The narrow Planetary Operations casualty move: remove wounded individuals from one
    /// surface region to a Chapter ship in orbit. Treatment choice remains Recovery Operations.
    /// </summary>
    public sealed class MedicalDetachmentService
    {
        private readonly IPhysicalPostingCommands _personnel;

        public MedicalDetachmentService(IPhysicalPostingCommands personnel) =>
            _personnel = personnel ?? throw new System.ArgumentNullException(nameof(personnel));

        public MedicalDetachmentResult DetachToOrbit(
            Sector sector,
            OnlyWar.Domain.Planets.Planet planet,
            OnlyWar.Domain.Planets.Region source,
            Ship destination,
            IReadOnlyList<PlayerSoldier> casualties,
            Date date)
        {
            List<PlayerSoldier> soldiers = (casualties ?? [])
                .Where(soldier => soldier != null)
                .DistinctBy(soldier => soldier.Id)
                .ToList();
            if (sector?.PlayerForce == null || planet == null || source?.Planet != planet
                || destination == null || soldiers.Count == 0)
            {
                return Failure("Select wounded personnel and a ship in orbit.");
            }
            if (!PlanetForceMovementService.GetOrbitingPlayerShips(
                    planet, sector.PlayerForce.Faction).Contains(destination))
            {
                return Failure("The destination ship is no longer available in orbit.");
            }
            if (destination.AvailableCapacity < soldiers.Count)
            {
                return Failure($"{destination.Name} is short {soldiers.Count - destination.AvailableCapacity} passenger spaces.");
            }
            foreach (PlayerSoldier soldier in soldiers)
            {
                string reason = null;
                if (!soldier.IsWounded
                    || soldier.IndividualPosting != null
                    || soldier.AssignedSquad?.CurrentRegion != source
                    || !_personnel.CanCreate(
                        soldier,
                        IndividualPostingPurpose.Medical,
                        CampaignLocation.Aboard(destination),
                        out reason))
                {
                    return Failure(reason ?? $"{soldier.Name} can no longer be detached from this region.");
                }
            }

            foreach (PlayerSoldier soldier in soldiers)
            {
                _personnel.BeginMedicalDetachment(
                    soldier, CampaignLocation.Aboard(destination), date);
            }
            return new MedicalDetachmentResult(
                true,
                soldiers.Count == 1
                    ? "Casualty detached to orbit."
                    : $"{soldiers.Count} casualties detached to orbit.",
                soldiers.Count);
        }

        private static MedicalDetachmentResult Failure(string message) => new(false, message);
    }
}
