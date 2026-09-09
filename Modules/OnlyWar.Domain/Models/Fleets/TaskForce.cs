using OnlyWar.Domain.Planets;
using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Abstractions;
using OnlyWar.Domain.Identity;

namespace OnlyWar.Domain.Fleets
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
        public int Id { get; set; }
        public Faction Faction { get; }
        public Coordinate? Position { get; set; }
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

        public TaskForce(int id, Faction faction, Coordinate? position,
                     Planet location, Planet destination, List<Ship> ships, int travelWeeksRemaining = 0,
                     Planet origin = null, FleetTravelPhase travelPhase = FleetTravelPhase.InOrbit,
                     int currentPhaseWeeksRemaining = 0, double warpSubjectiveWeeks = 0,
                     double warpObjectiveWeeks = 0, bool warpSubjectiveTrainingApplied = true)
        {
            Id = id;
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

        public TaskForce(
            Faction faction,
            FleetTemplate template,
            IPersistentIdAllocator identity) : this(faction, identity)
        {
            if (faction == null)
            {
                throw new ArgumentNullException(nameof(faction));
            }
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }
            if (template.Ships == null || template.Ships.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Fleet template '{template.Name}' has no ship templates.");
            }

            BoatTemplate boatTemplate = faction.BoatTemplates?.Values.FirstOrDefault();
            if (boatTemplate == null)
            {
                throw new InvalidOperationException(
                    $"Faction '{faction.Name}' has no boat template for fleet creation.");
            }

            int i = Id * 1000;
            foreach(ShipTemplate shipTemplate in template.Ships)
            {
                if (shipTemplate == null)
                {
                    throw new InvalidOperationException(
                        $"Fleet template '{template.Name}' contains a null ship template.");
                }
                Ship newShip = new Ship(i, $"{shipTemplate.ClassName}-{i}", shipTemplate, boatTemplate)
                {
                    Fleet = this
                };
                Ships.Add(newShip);
                i++;
            }
        }

        /// <summary>Compatibility overload backed by a fresh, non-global allocator.</summary>
        public TaskForce(Faction faction, FleetTemplate template)
            : this(faction, template, new CompatibilityPersistentIdAllocator())
        {
        }

        public TaskForce(Faction faction, IPersistentIdAllocator identity)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            Id = identity.GetNextTaskForceId();
            Faction = faction;
            Ships = [];
            TravelPhase = FleetTravelPhase.InOrbit;
            WarpSubjectiveTrainingApplied = true;
        }

        /// <summary>Compatibility overload backed by a fresh, non-global allocator.</summary>
        public TaskForce(Faction faction)
            : this(faction, new CompatibilityPersistentIdAllocator())
        {
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
            TravelWeeksRemaining = System.Math.Max(1, travelWeeks);
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
                + System.Math.Max(1, (int)System.Math.Ceiling(WarpObjectiveWeeks))
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
                        CurrentPhaseWeeksRemaining = System.Math.Max(1, (int)System.Math.Ceiling(WarpObjectiveWeeks));
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
