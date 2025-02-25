using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Extensions;
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
        public List<MissionContext> MissionContexts { get; private set; }
        public List<SpecialMission> SpecialMissions { get; private set; }

        public TurnController()
        {
            MissionContexts = new List<MissionContext>();
            SpecialMissions = new List<SpecialMission>();
        }

        public void ProcessTurn(Models.Sector sector)
        {
            MissionContexts.Clear();
            SpecialMissions.Clear();
            ProcessMissions(sector);
            UpdateIntelligence(sector.Planets.Values);
            // TODO: move this into a thread that can run while the player is interacting with the UI
            UpdatePlanetaryForcesPlans(sector.Planets.Values);
        }

        private void UpdateIntelligence(IEnumerable<Planet> planets)
        {
            foreach (Planet planet in planets)
            {
                foreach (Region region in planet.Regions)
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
                                SpecialMissions.Add(ass);
                            }
                            else if (chance >= 1)
                            {
                                // sabotage
                                SpecialMission sabotage = new SpecialMission(0, MissionType.Sabotage, region);
                                region.SpecialMissions.Add(sabotage);
                                SpecialMissions.Add(sabotage);
                                // plant minefield

                            }
                            else if (chance >= 0)
                            {
                                // ambush, equipment/prisoner recovery
                                SpecialMission ambush = new SpecialMission(0, MissionType.Ambush, region);
                                region.SpecialMissions.Add(ambush);
                                SpecialMissions.Add(ambush);
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

        private void ProcessMissions(Models.Sector sector)
        {
            foreach (Order order in sector.Orders.Values)
            {
                List<BattleSquad> playerBattleSquads = new List<BattleSquad>
                {
                    new BattleSquad(true, order.OrderedSquad)
                };
                MissionContext context = new MissionContext(order.TargetRegion, order.MissionType, order.LevelOfAggression, playerBattleSquads, new List<BattleSquad>());
                MissionStepOrchestrator.GetStartingStep(context).ExecuteMissionStep(context, 0, null);
                MissionContexts.Add(context);
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

        private void UpdatePlanetaryForcesPlans(IEnumerable<Planet> planets)
        {
            foreach (Planet planet in planets)
            {
                if(!planet.IsUnderAssault())
                {
                    continue;
                }
                foreach (Region region in planet.Regions)
                {
                    foreach (RegionFaction regionFaction in region.RegionFactionMap.Values)
                    {
                        if (regionFaction.PlanetFaction.Faction.IsPlayerFaction)
                        {
                            // Player forces are updated by the player
                            continue;
                        }
                        if(regionFaction.Organization == -1)
                        {
                            // initialize the region faction
                            regionFaction.Organization = 10.0f;
                            regionFaction.Detection = 0.0f;
                            regionFaction.Entrenchment = 0.0f;
                            regionFaction.AntiAir = 0.0f;
                        }
                        if (regionFaction.IsPublic)
                        {
                            UpdatePublicForcePlans(regionFaction);
                        }
                        else
                        {
                            UpdateHiddenForcePlans(regionFaction);
                        }
                    }
                }
            }
        }

        private void UpdateHiddenForcePlans(RegionFaction hiddenFaction)
        {
            // determine if there are public enemy forces in the region
            // determine if there are public enemy forces in adjacent regions
            // determine the forces available

        }

        private void UpdatePublicForcePlans(RegionFaction publicFaction)
        {
            // determine the forces available
            long organizedCap = (long)(publicFaction.Organization * 1000);
            long organizedTroops = Math.Min(publicFaction.Population, organizedCap);
            long disorganizedTroops = publicFaction.Population - organizedTroops;
            long garrisonRequirements = (int)(100 * (publicFaction.Detection + publicFaction.Entrenchment + publicFaction.AntiAir));
            if(garrisonRequirements == 0 && organizedTroops < 100)
            {
                // if there are no defenses and not enough organized troops to build any
                // remaining troops go into hiding
                publicFaction.IsPublic = false;
            }
            else if (garrisonRequirements < organizedTroops)
            {
                // there are spare troops for other activities
                long buildPointsAvailable = (organizedTroops - garrisonRequirements) / 100;
                // determine if there are public enemy forces in the region
                // if the organization is below some threshold, need to devote labor to improving that
                if(organizedCap < organizedTroops)
                {
                    // we should probably invest in some Organization

                }
                // we probably want some minimum amount of detection
                // after that, some amount of entrenchment
                // followed by some amount of anti-air
                // determine if there are public enemy forces in adjacent regions
            }
        }
    }
}
