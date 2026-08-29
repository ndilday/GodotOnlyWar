using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Fleets
{
    public class Boat
    {
        private readonly List<Squad> _loadedSquads;
        private static int _idGenerator = 10000;
        public int Id { get; }
        public string Name { get; }
        public BoatTemplate Template { get; }
        public IReadOnlyCollection<Squad> LoadedSoldiers { get => _loadedSquads; }

        public Boat(BoatTemplate template)
        {
            Id = _idGenerator++;
            Name = $"{template.ClassName}-{Id}";
            _loadedSquads = [];
            Template = template;
        }

        public void LoadSquad(Squad squad)
        {
            int loadedCount = _loadedSquads.Sum(ls => ls.Members.Count);
            if (squad.Members.Count + loadedCount > Template.SoldierCapacity)
            {
                throw new InvalidOperationException("Trying to load too many soldiers onto the ship");
            }
            _loadedSquads.Add(squad);
        }
    }

    public class Ship
    {
        private readonly List<Squad> _loadedSquads;
        private readonly List<Soldiers.PlayerSoldier> _individuallyBoardedSoldiers;
        private readonly List<Squad> _administrativeStations;

        public int Id { get; }
        public string Name { get; }
        public TaskForce Fleet { get; set; }
        public ShipTemplate Template { get; }
        /// <summary>Whether this is the unique player Chapter flagship.</summary>
        public bool IsFlagship { get; internal set; }
        public IReadOnlyCollection<Squad> LoadedSquads { get => _loadedSquads; }
        public IReadOnlyCollection<Soldiers.PlayerSoldier> IndividuallyBoardedSoldiers => _individuallyBoardedSoldiers;
        /// <summary>
        /// Administrative formations seated aboard this ship. These are intentionally not in
        /// LoadedSquads: they consume berths but never contribute a nominal combat squad to a
        /// fleet, regional control, or battle roster.
        /// </summary>
        public IReadOnlyCollection<Squad> AdministrativeStations => _administrativeStations;
        public List<Boat> Boats { get; }
        public int LoadedSoldierCount => Helpers.ShipCapacityService.LoadedSoldierCount(this);
        public int AvailableCapacity { get => Template.SoldierCapacity - LoadedSoldierCount; }

        public Ship(int id, string name, ShipTemplate template)
        {
            Id = id;
            Name = name;
            Template = template;
            Boats = [];
            _loadedSquads = [];
            _individuallyBoardedSoldiers = [];
            _administrativeStations = [];
        }

        public Ship(int id, string name, ShipTemplate template, BoatTemplate boatTemplate) 
            : this(id, name, template)
        {
            for (byte i = 0; i < Template.BoatCapacity; i++)
            {
                Boats.Add(new Boat(boatTemplate));
            }
        }

        public void LoadSquad(Squad squad)
        {
            if (_loadedSquads.Contains(squad))
            {
                return;
            }

            if (squad?.PermitsIndividualDeployment == true)
            {
                throw new InvalidOperationException(
                    "A MembersOnly administrative formation must use the administrative station manifest.");
            }

            int count = Helpers.SoldierPresenceService.PresentCount(squad);
            if (count + LoadedSoldierCount > Template.SoldierCapacity)
            {
                throw new InvalidOperationException("Trying to load too many soldiers onto the ship");
            }
            _loadedSquads.Add(squad);
        }

        public void RemoveSquad(Squad squad)
        {
            _loadedSquads.Remove(squad);
        }

        public void UnloadAllSquads()
        {
            _loadedSquads.Clear();
        }

        internal void StationAdministrativeFormation(Squad squad)
        {
            if (squad?.PermitsIndividualDeployment != true)
            {
                throw new InvalidOperationException(
                    "Only MembersOnly administrative formations may occupy an administrative station.");
            }
            if (_administrativeStations.Contains(squad)) return;
            if (Helpers.ShipCapacityService.AvailableCapacity(this)
                < Helpers.SoldierPresenceService.PresentCount(squad))
            {
                throw new InvalidOperationException(
                    $"{Name} has insufficient capacity for administrative stationing.");
            }
            _administrativeStations.Add(squad);
        }

        internal void RemoveAdministrativeFormation(Squad squad) =>
            _administrativeStations.Remove(squad);

        internal void BoardIndividual(Soldiers.PlayerSoldier soldier)
        {
            if (soldier == null || _individuallyBoardedSoldiers.Contains(soldier)) return;
            if (AvailableCapacity <= 0)
            {
                throw new InvalidOperationException("Trying to load too many soldiers onto the ship");
            }
            _individuallyBoardedSoldiers.Add(soldier);
        }

        internal void DisembarkIndividual(Soldiers.PlayerSoldier soldier) =>
            _individuallyBoardedSoldiers.Remove(soldier);
    }
}
