using OnlyWar.Domain.Extensions;
using OnlyWar.Domain;
using OnlyWar.Domain.Geometry;
using OnlyWar.Domain.Planets;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Runtime.WorldGeometry
{
    /// <summary>
    /// Deterministic derived sector state: the subsector layout, the warp-lane network and the
    /// governance designation. All three are pure functions of the persisted planet positions and
    /// the generation profile, so new game and load rebuild them through this same path rather than
    /// persisting them — which is why this is Runtime rather than Generation. Persistence and the
    /// application both call it without depending on new-game policy (SB-08/SB-09).
    /// </summary>
    public static class SectorTopologyBuilder
    {
        /// <summary>
        /// Rebuilds the subsector layout, warp-lane network and governance designation for a sector.
        /// Idempotent: rerunning it re-derives the same result from the same planet positions.
        /// </summary>
        public static void Rebuild(Sector sector, GameRulesData data)
        {
            SectorGenerationProfile profile = data.SectorGenerationProfile;
            GridCell gridDimensions = new(profile.SectorWidth, profile.SectorHeight);
            List<Subsector> subsectors = SubsectorBuilder.BuildSubsectors(
                sector.Planets.Values,
                gridDimensions,
                profile.MaxSubsectorDiameter);
            List<WarpLane> warpLanes = WarpLaneBuilder.BuildWarpLanes(
                subsectors,
                profile.MaxSubsectorDiameter * 2.5);
            sector.InitializeWarpNetwork(subsectors, warpLanes);
            AssignGovernance(sector);
        }

        /// <summary>
        /// Recomputes the governance designation (Design/Reference/OpeningScenario.md). For each
        /// subsector, the highest-Importance Imperial-controlled world becomes the governance
        /// seat (tagged SubsectorCapital); the top seat sector-wide is promoted to SectorCapital.
        /// Like the warp network, this is derived from persisted planet data rather than stored,
        /// so it is rebuilt on both new-game and load and is idempotent if rerun.
        /// </summary>
        public static void AssignGovernance(Sector sector)
        {
            // Clear any stale designation so reruns (load, end-of-turn refresh) re-derive cleanly.
            foreach (Planet planet in sector.Planets.Values)
            {
                planet.GovernanceTier = GovernanceTier.Planetary;
            }

            Planet sectorSeat = null;
            foreach (Subsector subsector in sector.Subsectors)
            {
                Planet seat = subsector.Planets
                    .Where(p => p.GetControllingFaction()?.IsDefaultFaction == true)
                    .OrderByDescending(p => p.Importance)
                    .ThenByDescending(p => p.Population)
                    .ThenBy(p => p.Id)
                    .FirstOrDefault();
                subsector.SetGovernanceSeat(seat);
                if (seat == null) continue;

                seat.GovernanceTier = GovernanceTier.SubsectorCapital;
                if (sectorSeat == null || OutranksSeat(seat, sectorSeat))
                {
                    sectorSeat = seat;
                }
            }

            // Promote the strongest subsector seat to the single sector capital.
            if (sectorSeat != null)
            {
                sectorSeat.GovernanceTier = GovernanceTier.SectorCapital;
            }
        }

        // Ranks two candidate governance seats by the same order used to pick a subsector seat:
        // Importance, then Population, then Id (so selection is deterministic for a seed).
        private static bool OutranksSeat(Planet candidate, Planet incumbent)
        {
            if (candidate.Importance != incumbent.Importance)
                return candidate.Importance > incumbent.Importance;
            if (candidate.Population != incumbent.Population)
                return candidate.Population > incumbent.Population;
            return candidate.Id < incumbent.Id;
        }
    }
}
