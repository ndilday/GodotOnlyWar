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
        private readonly FactionRelationshipLedger _relationshipLedger;

        public List<Character> Characters { get => _characters; }
        public IReadOnlyDictionary<int, Planet> Planets { get => _planets; }
        public IReadOnlyDictionary<ushort, List<Coordinate>> SubsectorPlanetMap { get => _subsectorPlanetMap; }
        public IReadOnlyDictionary<ushort, Coordinate> SubsectorCenterMap { get => _subsectorCenterMap; }
        public IReadOnlyList<Subsector> Subsectors { get => _subsectors; }
        public IReadOnlyList<WarpLane> WarpLanes { get => _warpLanes; }
        public IReadOnlyDictionary<int, TaskForce> Fleets { get => _fleets; }
        public IReadOnlyDictionary<int, Order> Orders { get => _orders; }
        public PlayerForce PlayerForce { get; }
        public FactionRelationshipLedger RelationshipLedger => _relationshipLedger;
        public FactionRelationshipLedger FactionRelationships => _relationshipLedger;

        // The framed opening scenario stamped onto this sector at generation
        // (Design/Reference/OpeningScenario.md). Null for plain-sandbox sectors,
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
            _relationshipLedger = new FactionRelationshipLedger();
        }

        public Sector(PlayerForce playerForce, List<Character> characters, List<Planet> planets, List<TaskForce> fleets,
                      FactionRelationshipLedger relationshipLedger = null)
            : this()
        {
            if (relationshipLedger != null)
            {
                _relationshipLedger = relationshipLedger;
            }
            PlayerForce = playerForce;
            _characters.AddRange(characters);

            foreach (Planet planet in planets)
            {
                _planets[planet.Id] = planet;
                planet.AttachRelationshipLedger(_relationshipLedger);
                planet.PlanetFactionAdded += OnPlanetFactionAdded;
                foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values)
                {
                    AttachFactionIntelEvents(planetFaction);
                }
            }

            SeedDefaultImperialAlliance();
            _relationshipLedger.StanceChanged += OnStanceChanged;

            foreach (TaskForce fleet in fleets)
            {
                _fleets[fleet.Id] = fleet;
                if (fleet.Planet != null)
                {
                    fleet.Planet.OrbitingTaskForceList.Add(fleet);
                }
            }
        }

        private void SeedDefaultImperialAlliance()
        {
            Faction playerFaction = PlayerForce?.Faction;
            Faction defaultFaction = _planets.Values
                .SelectMany(planet => planet.PlanetFactionMap.Values)
                .Select(planetFaction => planetFaction.Faction)
                .FirstOrDefault(faction => faction.IsDefaultFaction);
            if (playerFaction == null || defaultFaction == null || playerFaction.Id == defaultFaction.Id)
            {
                return;
            }

            // Generation/load establishes this before any region-level relationship query runs.
            if (_relationshipLedger.GetStance(playerFaction, defaultFaction) == FactionStance.Hostile)
            {
                _relationshipLedger.SetStance(playerFaction, defaultFaction, FactionStance.Allied);
            }
        }

        private void OnPlanetFactionAdded(object sender, PlanetFaction planetFaction)
        {
            AttachFactionIntelEvents(planetFaction);
        }

        private void AttachFactionIntelEvents(PlanetFaction planetFaction)
        {
            if (planetFaction == null) return;
            planetFaction.TargetIntelChanged -= OnTargetIntelChanged;
            planetFaction.TargetIntelChanged += OnTargetIntelChanged;
        }

        private void OnTargetIntelChanged(object sender, FactionIntelChangedEventArgs change)
        {
            if (PlayerForce?.CampaignEventRecorder == null
                || sender is not PlanetFaction observer
                || (!observer.Faction.IsPlayerFaction && !observer.Faction.IsDefaultFaction))
            {
                return;
            }

            FactionIntelBelief belief = change.Current ?? change.Previous;
            if (belief?.Region?.Planet == null) return;
            PlayerForce.CampaignEventRecorder.RecordFactionIntel(
                change,
                belief.Region.Planet.Id,
                change.Observation.EvidenceWeek);
        }

        private void OnStanceChanged(object sender, FactionRelationshipChangedEventArgs change)
        {
            if (PlayerForce?.CampaignEventRecorder == null) return;
            if (!_relationshipLedger.KnownFactions.TryGetValue(
                    change.Pair.LowerFactionId,
                    out Faction lowerFaction)
                || !_relationshipLedger.KnownFactions.TryGetValue(
                    change.Pair.HigherFactionId,
                    out Faction higherFaction))
            {
                return;
            }

            int occurredWeek = GameDataSingleton.Instance.Date?.GetTotalWeeks() ?? 0;
            PlayerForce.CampaignEventRecorder.RecordFactionRelationship(
                change,
                lowerFaction,
                higherFaction,
                occurredWeek);
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
        // SectorBuilder.GenerateWarpNetwork (Design/Reference/OpeningScenario.md).

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
