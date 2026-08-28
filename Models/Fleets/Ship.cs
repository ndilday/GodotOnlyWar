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

        public int Id { get; }
        public string Name { get; }
        public TaskForce Fleet { get; set; }
        public ShipTemplate Template { get; }
        public IReadOnlyCollection<Squad> LoadedSquads { get => _loadedSquads; }
        public IReadOnlyCollection<Soldiers.PlayerSoldier> IndividuallyBoardedSoldiers => _individuallyBoardedSoldiers;
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
