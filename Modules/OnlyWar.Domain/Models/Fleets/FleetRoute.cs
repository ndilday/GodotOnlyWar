using OnlyWar.Models.Planets;
using System.Collections.Generic;

namespace OnlyWar.Models.Fleets
{
    public enum FleetRouteType
    {
        Direct = 0,
        WarpLane = 1
    }

    public enum FleetRouteScope
    {
        SameSubsector = 0,
        AdjacentSubsector = 1,
        DistantSubsector = 2
    }

    public class FleetRoute
    {
        public FleetRouteType RouteType { get; }
        public FleetRouteScope Scope { get; }
        public IReadOnlyList<Planet> Hops { get; }
        public double TotalDistance { get; }
        public int BaseWarpWeeks { get; }
        public double SubjectiveWarpWeeks { get; }
        public double ObjectiveWarpWeeks { get; }
        public double SubjectiveTotalWeeks { get; }
        public double ObjectiveTotalWeeks { get; }
        public int BaseTurns { get; }
        public int EstimatedMinTurns { get; }
        public int EstimatedMaxTurns { get; }

        public FleetRoute(FleetRouteType routeType,
                          FleetRouteScope scope,
                          IReadOnlyList<Planet> hops,
                          double totalDistance,
                          int baseWarpWeeks,
                          double subjectiveWarpWeeks,
                          double objectiveWarpWeeks,
                          double subjectiveTotalWeeks,
                          double objectiveTotalWeeks,
                          int baseTurns,
                          int estimatedMinTurns,
                          int estimatedMaxTurns)
        {
            RouteType = routeType;
            Scope = scope;
            Hops = hops;
            TotalDistance = totalDistance;
            BaseWarpWeeks = baseWarpWeeks;
            SubjectiveWarpWeeks = subjectiveWarpWeeks;
            ObjectiveWarpWeeks = objectiveWarpWeeks;
            SubjectiveTotalWeeks = subjectiveTotalWeeks;
            ObjectiveTotalWeeks = objectiveTotalWeeks;
            BaseTurns = baseTurns;
            EstimatedMinTurns = estimatedMinTurns;
            EstimatedMaxTurns = estimatedMaxTurns;
        }
    }
}
