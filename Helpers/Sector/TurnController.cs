﻿using OnlyWar.Builders;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Sector
{
    class TurnController
    {
        public TurnController()
        {
        }

        public void ProcessTurn(Models.Sector sector)
        {
            foreach(Order order in sector.Orders.Values)
            {
                MissionContext context = new MissionContext(order.TargetRegion, order.MissionType, new List<Squad> { order.OrderedSquad }, new List<Squad>());
                MissionStepOrchestrator.GetStartingStep(context).ExecuteMissionStep(context, 0, null);
            }
            foreach(Planet planet in sector.Planets.Values)
            {
                foreach(Region region in planet.Regions)
                {
                    // 25% chance of unexecuted special missions being removed
                    foreach (SpecialMission specialMission in region.SpecialMissions)
                    {
                        if (RNG.GetIntBelowMax(0, 4) == 0)
                        {
                            region.SpecialMissions.Remove(specialMission);
                        }
                    }
                    if (region.IntelligenceLevel > 0)
                    {
                        // see if any intelligence gets spent in exchange for special mission opportunities
                        float specMissionChance = (float)Math.Log(region.IntelligenceLevel, 2) + 1;
                        // subtract one for each special mission already identified
                        specMissionChance -= region.SpecialMissions.Count;
                        for (int i = 0; i < specMissionChance; i++)
                        {
                            double chance = RNG.NextRandomZValue();
                            // TODO: add some kind of recon data to the context
                            // do some sort of test to see whether a special mission opportunity is found
                            // if not, improve the inteligence level by the margin
                            if (chance >= 2)
                            {
                                // assassination
                                SpecialMission ass = new SpecialMission(0, MissionType.Assassination, region);
                                region.SpecialMissions.Add(ass);
                            }
                            else if (chance >= 1)
                            {
                                // sabotage
                                SpecialMission sabotage = new SpecialMission(0, MissionType.Sabotage, region);
                                region.SpecialMissions.Add(sabotage);
                                // plant minefield

                            }
                            else if (chance >= 0)
                            {
                                // ambush, equipment/prisoner recovery
                                SpecialMission ambush = new SpecialMission(0, MissionType.Ambush, region);
                                region.SpecialMissions.Add(ambush);
                                // sniper's nest
                                // prisoner recovery
                                // equipment recovery

                            }
                        }
                        // reduce intelligence level by 25%
                        region.IntelligenceLevel *= 0.75f;
                    }
                }
            }
        }

        private Dictionary<Region, List<Squad>> MapSquadsToTargetRegions(Planet planet)
        {
            // Get all squads on the planet (in regions)
            var planetSquads = planet.Regions
                .SelectMany(region => region.RegionFactionMap.Values)
                .SelectMany(rf => rf.LandedSquads)
                .Where(s => s.CurrentOrders != null && s.CurrentOrders.TargetRegion != null);

            // Get all squads in fleets orbiting the planet
            var fleetSquads = planet.OrbitingTaskForceList
                .SelectMany(tf => tf.Ships)
                .SelectMany(ship => ship.LoadedSquads)
                .Where(s => s.CurrentOrders != null && s.CurrentOrders.TargetRegion != null);

            // Combine the two lists
            var allSquads = planetSquads.Concat(fleetSquads);

            // Group squads by their target region based on their orders
            var squadsByTargetRegion = allSquads
                .GroupBy(squad => squad.CurrentOrders.TargetRegion)
                .ToDictionary(group => group.Key, group => group.ToList());

            return squadsByTargetRegion;
        }
    }
}
