using OnlyWar.Models.Fleets;
using OnlyWar.Models.Orders;
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
        private readonly Dictionary<ushort, List<Coordinate>> _subsectorPlanetMap;
        private readonly Dictionary<ushort, Coordinate> _subsectorCenterMap;
        private readonly List<Subsector> _subsectors;
        private readonly List<WarpLane> _warpLanes;
        private readonly List<Character> _characters;
        private readonly Dictionary<int, Order> _orders;

        public List<Character> Characters { get => _characters; }
        public IReadOnlyDictionary<int, Planet> Planets { get => _planets; }
        public IReadOnlyDictionary<ushort, List<Coordinate>> SubsectorPlanetMap { get => _subsectorPlanetMap; }
        public IReadOnlyDictionary<ushort, Coordinate> SubsectorCenterMap { get => _subsectorCenterMap; }
        public IReadOnlyList<Subsector> Subsectors { get => _subsectors; }
        public IReadOnlyList<WarpLane> WarpLanes { get => _warpLanes; }
        public IReadOnlyDictionary<int, TaskForce> Fleets { get => _fleets; }
        public IReadOnlyDictionary<int, Order> Orders { get => _orders; }
        public PlayerForce PlayerForce { get; }

        // The framed opening scenario stamped onto this sector at generation
        // (Design/OpeningScenario.md §2.1). Null for plain-sandbox sectors,
        // in which case the game behaves as it did before the Opening Scenario work.
        public CampaignScenario Scenario { get; set; }
        

        public Sector()
        {
            
            _characters = [];
            _planets = [];
            _fleets = [];
            _subsectorPlanetMap = [];
            _subsectorCenterMap = [];
            _subsectors = [];
            _warpLanes = [];
            _orders = [];
        }

        public Sector(PlayerForce playerForce, List<Character> characters, List<Planet> planets, List<TaskForce> fleets) 
            : this()
        {
            PlayerForce = playerForce;
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
                    fleet.Planet.OrbitingTaskForceList.Add(fleet);
                }
            }
        }

        public void InitializeWarpNetwork(IEnumerable<Subsector> subsectors, IEnumerable<WarpLane> warpLanes)
        {
            _subsectors.Clear();
            _subsectors.AddRange(subsectors);
            _warpLanes.Clear();
            _warpLanes.AddRange(warpLanes);
        }

        public Planet GetPlanet(int planetId)
        {
            return Planets[planetId];
        }

        // Governance resolvers over the derived designation set by
        // SectorBuilder.GenerateWarpNetwork (Design/OpeningScenario.md §2.3).

        // The single SectorCapital-tier world, or null if no Imperial world qualifies.
        public Planet GetSectorCapital()
        {
            return _planets.Values.SingleOrDefault(p => p.GovernanceTier == GovernanceTier.SectorCapital);
        }

        // The Sector Lord: the governor seated on the sector capital.
        public Character GetSectorLord()
        {
            return GetSectorCapital()?.Governor;
        }

        // The governor seated on a subsector's seat of government.
        public Character GetSubsectorGovernor(Subsector subsector)
        {
            return subsector?.GovernanceSeat?.Governor;
        }

        public Planet GetPlanetByPosition(Coordinate worldPosition)
        {
            return Planets.Values.Where(p => p.Position.Equals(worldPosition)).SingleOrDefault();
        }

        public IEnumerable<TaskForce> GetFleetsByPosition(Coordinate worldPosition)
        {
            return Fleets.Values.Where(f => f.Position != null && f.Position.Value.Equals(worldPosition));
        }

        public void AddNewFleet(TaskForce newFleet)
        {
            _fleets[newFleet.Id] = newFleet;
            if (newFleet.Planet != null)
            {
                newFleet.Planet.OrbitingTaskForceList.Add(newFleet);
            }
        }

        public void AddNewOrder(Order newOrder)
        {
            _orders[newOrder.Id] = newOrder;
        }

        public void RemoveOrder(Order existingOrder)
        {
            if(_orders.ContainsKey(existingOrder.Id))
            {
                _orders.Remove(existingOrder.Id);
            }
        }

        public void CombineFleets(TaskForce remainingFleet, TaskForce mergingFleet)
        {
            if (mergingFleet.Planet != remainingFleet.Planet
                || !Equals(mergingFleet.Position, remainingFleet.Position)
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
            mergingFleet.Planet.OrbitingTaskForceList.Remove(mergingFleet);
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
                newFleet.Planet.OrbitingTaskForceList.Add(newFleet);
            }
            _fleets[newFleet.Id] = newFleet;
            return newFleet;
        }
    }
}
