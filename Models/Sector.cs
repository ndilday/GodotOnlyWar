using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;


namespace OnlyWar.Models
{
    public class Sector
    {
        private readonly Dictionary<int, TaskForce> _fleets;
        private readonly Dictionary<int, Planet> _planets;
        private readonly Dictionary<ushort, List<Tuple<ushort, ushort>>> _subsectorPlanetMap;
        private readonly Dictionary<ushort, Tuple<ushort, ushort>> _subsectorCenterMap;
        private readonly List<Character> _characters;
        
        public List<Character> Characters { get => _characters; }
        public IReadOnlyDictionary<int, Planet> Planets { get => _planets; }
        public IReadOnlyDictionary<ushort, List<Tuple<ushort, ushort>>> SubsectorPlanetMap { get => _subsectorPlanetMap; }
        public IReadOnlyDictionary<ushort, Tuple<ushort, ushort>> SubsectorCenterMap { get => _subsectorCenterMap; }
        public IReadOnlyDictionary<int, TaskForce> Fleets { get => _fleets; }
        

        public Sector()
        {
            
            _characters = new List<Character>();
            _planets = new Dictionary<int, Planet>();
            _fleets = new Dictionary<int, TaskForce>();
            _subsectorPlanetMap = new Dictionary<ushort, List<Tuple<ushort, ushort>>>();
            _subsectorCenterMap = new Dictionary<ushort, Tuple<ushort, ushort>>();
        }

        public Sector(List<Character> characters, List<Planet> planets, List<TaskForce> fleets) 
            : this()
        {
            _characters.AddRange(characters);

            foreach (Planet planet in planets)
            {
                _planets[planet.Id] = planet;
            }

            foreach (TaskForce fleet in fleets)
            {
                _fleets[fleet.Id] = fleet;
                if (fleet.Planet != null)
                {
                    fleet.Planet.TaskForces.Add(fleet);
                }
            }
        }

        public Planet GetPlanet(int planetId)
        {
            return Planets[planetId];
        }

        public Planet GetPlanetByPosition(Tuple<ushort, ushort> worldPosition)
        {
            return Planets.Values.Where(p => p.Position != null && p.Position == worldPosition).SingleOrDefault();
        }

        public IEnumerable<TaskForce> GetFleetsByPosition(Tuple<ushort, ushort> worldPosition)
        {
            return Fleets.Values.Where(f => f.Position == worldPosition);
        }

        public void AddNewFleet(TaskForce newFleet)
        {
            _fleets[newFleet.Id] = newFleet;
            if (newFleet.Planet != null)
            {
                newFleet.Planet.TaskForces.Add(newFleet);
            }
        }

        public void CombineFleets(TaskForce remainingFleet, TaskForce mergingFleet)
        {
            if (mergingFleet.Planet != remainingFleet.Planet
                || mergingFleet.Position != remainingFleet.Position
                || mergingFleet.Faction.Id != remainingFleet.Faction.Id)
            {
                throw new InvalidOperationException("The two fleets cannot be merged");
            }
            foreach (Ship ship in mergingFleet.Ships)
            {
                remainingFleet.Ships.Add(ship);
                ship.Fleet = remainingFleet;
            }
            mergingFleet.Ships.Clear();
            remainingFleet.Ships.Sort((x, y) => x.Template.Id.CompareTo(y.Template.Id));
            _fleets.Remove(mergingFleet.Id);
            mergingFleet.Planet.TaskForces.Remove(mergingFleet);
        }

        public TaskForce SplitOffNewFleet(TaskForce originalFleet,
                                      IReadOnlyCollection<Ship> newFleetShipList)
        {
            TaskForce newFleet = new TaskForce(originalFleet.Faction)
            {
                Planet = originalFleet.Planet,
                Position = originalFleet.Position,
                Destination = originalFleet.Destination
            };
            foreach (Ship ship in newFleetShipList)
            {
                originalFleet.Ships.Remove(ship);
                newFleet.Ships.Add(ship);
                ship.Fleet = newFleet;
            }
            if (newFleet.Planet != null)
            {
                newFleet.Planet.TaskForces.Add(newFleet);
            }
            _fleets[newFleet.Id] = newFleet;
            return newFleet;
        }

        public void TakeControlOfPlanet(Planet planet, Faction faction)
        {
            planet.ControllingFaction = faction;
        }
    }
}