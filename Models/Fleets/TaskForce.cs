using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Fleets
{
    public enum FleetTravelPhase
    {
        InOrbit = 0,
        OutboundSystemTransit = 1,
        InWarp = 2,
        InboundSystemTransit = 3
    }

    public class FleetTravelAdvanceResult
    {
        public static FleetTravelAdvanceResult None { get; } = new();
        public bool ExitedWarp { get; set; }
        public double WarpSubjectiveWeeksElapsed { get; set; }
    }

    public class TaskForce
    {
        public const int SystemTransitWeeksPerEnd = 2;
        private static int _nextTaskForceId = 0;
        public int Id { get; set; }
        public Faction Faction { get; }
        public Tuple<ushort, ushort> Position { get; set; }
        public Planet Origin { get; set; }
        public Planet Destination { get; set; }
        public Planet Planet { get; set; }
        public FleetTravelPhase TravelPhase { get; set; }
        public int TravelWeeksRemaining { get; set; }
        public int CurrentPhaseWeeksRemaining { get; set; }
        public double WarpSubjectiveWeeks { get; set; }
        public double WarpObjectiveWeeks { get; set; }
        public bool WarpSubjectiveTrainingApplied { get; set; }
        public List<Ship> Ships { get; }

        public TaskForce(int id, Faction faction, Tuple<ushort, ushort> position, 
                     Planet location, Planet destination, List<Ship> ships, int travelWeeksRemaining = 0,
                     Planet origin = null, FleetTravelPhase travelPhase = FleetTravelPhase.InOrbit,
                     int currentPhaseWeeksRemaining = 0, double warpSubjectiveWeeks = 0,
                     double warpObjectiveWeeks = 0, bool warpSubjectiveTrainingApplied = true)
        {
            Id = id;
            if(_nextTaskForceId <= id)
            {
                _nextTaskForceId = id + 1;
            }
            Faction = faction;
            Position = position;
            Origin = origin;
            Planet = location;
            Destination = destination;
            TravelPhase = destination == null ? FleetTravelPhase.InOrbit : travelPhase;
            TravelWeeksRemaining = travelWeeksRemaining;
            CurrentPhaseWeeksRemaining = currentPhaseWeeksRemaining;
            WarpSubjectiveWeeks = warpSubjectiveWeeks;
            WarpObjectiveWeeks = warpObjectiveWeeks;
            WarpSubjectiveTrainingApplied = warpSubjectiveTrainingApplied;
            Ships = ships;
            foreach(Ship ship in ships)
            {
                ship.Fleet = this;
            }
        }

        public TaskForce(Faction faction, FleetTemplate template) : this(faction)
        {
            int i = Id * 1000;
            BoatTemplate boatTemplate = faction.BoatTemplates.First().Value;
            foreach(ShipTemplate shipTemplate in template.Ships)
            {
                Ship newShip = new Ship(i, $"{shipTemplate.ClassName}-{i}", shipTemplate, boatTemplate)
                {
                    Fleet = this
                };
                Ships.Add(newShip);
                i++;
            }
        }

        public TaskForce(Faction faction)
        {
            Id = _nextTaskForceId++;
            Faction = faction;
            Ships = [];
            TravelPhase = FleetTravelPhase.InOrbit;
            WarpSubjectiveTrainingApplied = true;
        }

        public void OrderMoveTo(Planet destination, int travelWeeks)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (Planet == null)
            {
                throw new InvalidOperationException("Task force must be in orbit before plotting a new movement order.");
            }
            if (Planet == destination)
            {
                throw new InvalidOperationException("Task force is already at the destination planet.");
            }

            Planet.OrbitingTaskForceList.Remove(this);
            Destination = destination;
            Planet = null;
            Origin = null;
            TravelPhase = FleetTravelPhase.InWarp;
            TravelWeeksRemaining = Math.Max(1, travelWeeks);
            CurrentPhaseWeeksRemaining = TravelWeeksRemaining;
            WarpSubjectiveWeeks = 0;
            WarpObjectiveWeeks = TravelWeeksRemaining;
            WarpSubjectiveTrainingApplied = true;
        }

        public void OrderMoveTo(Planet destination, FleetRoute route)
        {
            if (route == null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (Planet == null)
            {
                throw new InvalidOperationException("Task force must be in orbit before plotting a new movement order.");
            }
            if (Planet == destination)
            {
                throw new InvalidOperationException("Task force is already at the destination planet.");
            }

            Origin = Planet;
            Planet.OrbitingTaskForceList.Remove(this);
            Destination = destination;
            Planet = null;
            TravelPhase = FleetTravelPhase.OutboundSystemTransit;
            CurrentPhaseWeeksRemaining = SystemTransitWeeksPerEnd;
            WarpSubjectiveWeeks = route.SubjectiveWarpWeeks;
            WarpObjectiveWeeks = route.ObjectiveWarpWeeks;
            WarpSubjectiveTrainingApplied = false;
            TravelWeeksRemaining = SystemTransitWeeksPerEnd
                + Math.Max(1, (int)Math.Ceiling(WarpObjectiveWeeks))
                + SystemTransitWeeksPerEnd;
        }

        public FleetTravelAdvanceResult AdvanceTravelOneWeek()
        {
            if (Destination == null || TravelWeeksRemaining <= 0) return FleetTravelAdvanceResult.None;

            TravelWeeksRemaining--;
            if (CurrentPhaseWeeksRemaining > 0)
            {
                CurrentPhaseWeeksRemaining--;
            }

            switch (TravelPhase)
            {
                case FleetTravelPhase.OutboundSystemTransit:
                    if (CurrentPhaseWeeksRemaining <= 0)
                    {
                        TravelPhase = FleetTravelPhase.InWarp;
                        CurrentPhaseWeeksRemaining = Math.Max(1, (int)Math.Ceiling(WarpObjectiveWeeks));
                    }
                    break;
                case FleetTravelPhase.InWarp:
                    if (CurrentPhaseWeeksRemaining <= 0)
                    {
                        if (Origin == null)
                        {
                            CompleteTravel();
                            break;
                        }

                        TravelPhase = FleetTravelPhase.InboundSystemTransit;
                        CurrentPhaseWeeksRemaining = SystemTransitWeeksPerEnd;
                        return new FleetTravelAdvanceResult
                        {
                            ExitedWarp = !WarpSubjectiveTrainingApplied,
                            WarpSubjectiveWeeksElapsed = WarpSubjectiveTrainingApplied ? 0 : WarpSubjectiveWeeks
                        };
                    }
                    break;
                case FleetTravelPhase.InboundSystemTransit:
                    if (CurrentPhaseWeeksRemaining <= 0)
                    {
                        CompleteTravel();
                    }
                    break;
                default:
                    if (TravelWeeksRemaining <= 0)
                    {
                        CompleteTravel();
                    }
                    break;
            }

            return FleetTravelAdvanceResult.None;
        }

        private void CompleteTravel()
        {
            Planet = Destination;
            Position = Destination.Position;
            Destination = null;
            Origin = null;
            TravelPhase = FleetTravelPhase.InOrbit;
            TravelWeeksRemaining = 0;
            CurrentPhaseWeeksRemaining = 0;
            WarpSubjectiveWeeks = 0;
            WarpObjectiveWeeks = 0;
            WarpSubjectiveTrainingApplied = true;
            if (!Planet.OrbitingTaskForceList.Contains(this))
            {
                Planet.OrbitingTaskForceList.Add(this);
            }
        }
    }
}
