using OnlyWar.Domain;
using OnlyWar.Domain.Extensions;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Operations.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign
{
    public sealed record MechanicusReturnEntry(int SoldierId, string SoldierName, string Destination);

    /// <summary>What one turn's returns from Mars did, for the end-of-turn report.</summary>
    public sealed record MechanicusReturnReport(
        IReadOnlyList<MechanicusReturnEntry> Returns,
        bool LoanEnded);

    /// <summary>
    /// Brings Techmarines home from Mars (TDD §6.14, "Return"). A brother whose
    /// expected return date has come reports to the chapter's destination for returnees: the
    /// player's standing override if set, else the Home World's capital region, else the
    /// flagship. If that place no longer exists, or cannot take him, he rejoins the Armory at its
    /// duty station. The Mechanicus loan ends when the chapter's first Techmarines are home.
    /// </summary>
    public sealed class MechanicusReturnService
    {
        private readonly IndividualPostingService _postings;

        public MechanicusReturnService(IOrderCommitmentSurface commitments)
        {
            _postings = new IndividualPostingService(
                commitments ?? throw new ArgumentNullException(nameof(commitments)));
        }

        public static bool IsDue(PlayerSoldier soldier, Date currentDate) =>
            MechanicusTrainingService.IsOnMars(soldier)
            && soldier.IndividualPosting.ExpectedReturnDate != null
            && soldier.IndividualPosting.ExpectedReturnDate.GetTotalWeeks() <= currentDate.GetTotalWeeks();

        /// <summary>Returns every brother who is due, or null if nobody is.</summary>
        public MechanicusReturnReport ProcessReturns(Sector sector, Date currentDate)
        {
            ArgumentNullException.ThrowIfNull(currentDate);
            PlayerForce force = sector?.PlayerForce;
            List<PlayerSoldier> due = force?.Army?.PlayerSoldierMap?.Values
                .Where(soldier => IsDue(soldier, currentDate))
                .OrderBy(soldier => soldier.Id)
                .ToList() ?? [];
            if (due.Count == 0)
            {
                return null;
            }

            // Read before the postings change: the returning group's departure identifies who is
            // still to come home from the same or an earlier departure.
            int latestDeparture = due.Max(soldier => soldier.IndividualPosting.StartedDate.GetTotalWeeks());
            List<MechanicusReturnEntry> entries = due
                .Select(soldier => Return(sector, soldier, currentDate))
                .ToList();

            bool loanEnded = false;
            if (force.IsMechanicusLoanActive
                && !force.Army.PlayerSoldierMap.Values.Any(soldier =>
                    MechanicusTrainingService.IsOnMars(soldier)
                    && soldier.IndividualPosting.StartedDate.GetTotalWeeks() <= latestDeparture))
            {
                force.IsMechanicusLoanActive = false;
                loanEnded = true;
            }
            return new MechanicusReturnReport(entries, loanEnded);
        }

        /// <summary>
        /// Where a returnee reports: the chosen destination if it still exists, else null, which
        /// means the Armory's duty station.
        /// </summary>
        public static CampaignLocation ResolveDestination(Sector sector)
        {
            PlayerForce force = sector?.PlayerForce;
            if (force == null) return null;
            CampaignLocation chosen = force.MarsReturnDestination ?? DefaultDestination(sector);
            return Exists(sector, chosen) ? chosen : null;
        }

        private MechanicusReturnEntry Return(Sector sector, PlayerSoldier soldier, Date currentDate)
        {
            CampaignLocation destination = ResolveDestination(sector);
            CampaignLocation dutyStation = soldier.AssignedSquad?.DutyStation;
            if (destination == null
                || dutyStation?.IsSamePlace(destination) == true
                || !_postings.CanCreate(soldier, IndividualPostingPurpose.Independent, destination, out _))
            {
                _postings.EndOffSectorPosting(soldier);
                destination = dutyStation;
            }
            else
            {
                _postings.Create(soldier, IndividualPostingPurpose.Independent, destination, currentDate);
            }

            string place = Describe(destination, soldier);
            soldier.AddEvent(new SoldierEvent(currentDate, SoldierEventType.Transfer,
                $"returned from Mars as a trained {soldier.Template?.Name ?? "Techmarine"}, "
                + $"reporting to {place}"));
            return new MechanicusReturnEntry(soldier.Id, soldier.Name, place);
        }

        // The Home World's capital region while the chapter holds it, else the flagship.
        private static CampaignLocation DefaultDestination(Sector sector)
        {
            PlayerForce force = sector.PlayerForce;
            if (force.HomeWorldPlanetId is int homeWorldId
                && sector.Planets.TryGetValue(homeWorldId, out Planet homeWorld)
                && homeWorld.GetControllingFaction() == force.Faction)
            {
                CampaignLocation capital = CampaignLocation.Landed(homeWorld.GetCapitalRegion());
                if (capital != null) return capital;
            }
            return CampaignLocation.Aboard(PlayerShips(force).FirstOrDefault(ship => ship.IsFlagship));
        }

        private static bool Exists(Sector sector, CampaignLocation location)
        {
            if (location?.Ship != null)
            {
                return PlayerShips(sector.PlayerForce).Contains(location.Ship);
            }
            if (location?.Region != null)
            {
                return sector.Planets.TryGetValue(location.Region.Planet?.Id ?? -1, out Planet planet)
                    && planet.Regions.Contains(location.Region);
            }
            return false;
        }

        private static IEnumerable<Ship> PlayerShips(PlayerForce force) =>
            force.Fleet?.TaskForces.SelectMany(taskForce => taskForce.Ships) ?? [];

        private static string Describe(CampaignLocation location, PlayerSoldier soldier)
        {
            if (location?.Region != null)
            {
                return location.Region.Planet == null
                    ? location.Region.Name
                    : $"{location.Region.Name}, {location.Region.Planet.Name}";
            }
            if (location?.Ship != null)
            {
                return $"the {location.Ship.Name}";
            }
            return soldier.AssignedSquad?.Name ?? "the Armory";
        }
    }
}
