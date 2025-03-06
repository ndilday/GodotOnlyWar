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
            // TODO: move this into a thread that can run while the player is interacting with the UI
            ApplyRegionalFactionActivities(sector.Planets.Values);
            UpdateIntelligence(sector.Planets.Values);
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
                        RegionFaction enemyRegionFaction = region.RegionFactionMap.Values
                            .Where(rf => !rf.PlanetFaction.Faction.IsPlayerFaction && !rf.PlanetFaction.Faction.IsDefaultFaction).First();
                        if(enemyRegionFaction != null)
                        {
                            if(enemyRegionFaction.IsPublic)
                            {
                                HandlePublicFactionIntelligence(region, enemyRegionFaction);
                            }
                            else
                            {
                                HandleHiddenFactionIntelligence();
                            }
                        }
                        
                        // reduce intelligence level by 25%
                        region.IntelligenceLevel *= 0.75f;
                    }
                }
            }
        }

        public void HandlePublicFactionIntelligence(Region region, RegionFaction enemyRegionFaction)
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
                    // add up the amount of entrenchment, detection, and antiair in this region
                    int defenseTotal = enemyRegionFaction.Entrenchment + enemyRegionFaction.Detection + enemyRegionFaction.AntiAir;
                    if (defenseTotal == 0)
                    {
                        //make it an ambush, instead
                        SpecialMission ambush = new SpecialMission(0, MissionType.Ambush, region);
                        region.SpecialMissions.Add(ambush);
                        SpecialMissions.Add(ambush);
                    }
                    else
                    {
                        int roll = RNG.GetIntBelowMax(0, defenseTotal);
                        if (roll <= enemyRegionFaction.Entrenchment)
                        {
                            // saborage the entrenchments
                            int size = Math.Min(Math.Max((int)RNG.NextRandomZValue() + 1, 1), enemyRegionFaction.Entrenchment);
                            SabotageMission sabotage = new SabotageMission(0, DefenseType.Entrenchment, size, region);
                            region.SpecialMissions.Add(sabotage);
                            SpecialMissions.Add(sabotage);
                        }
                        else
                        {
                            roll -= enemyRegionFaction.Entrenchment;
                            if (roll <= enemyRegionFaction.Detection)
                            {
                                // sabotage the detection
                                int size = Math.Min(Math.Max((int)RNG.NextRandomZValue() + 1, 1), enemyRegionFaction.Detection);
                                SabotageMission sabotage = new SabotageMission(0, DefenseType.Detection, size, region);
                                region.SpecialMissions.Add(sabotage);
                                SpecialMissions.Add(sabotage);
                            }
                            else
                            {
                                // sabotage the antiair
                                int size = Math.Min(Math.Max((int)RNG.NextRandomZValue() + 1, 1), enemyRegionFaction.AntiAir);
                                SabotageMission sabotage = new SabotageMission(0, DefenseType.AntiAir, size, region);
                                region.SpecialMissions.Add(sabotage);
                                SpecialMissions.Add(sabotage);
                            }
                        }
                    }
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
        }

        public void HandleHiddenFactionIntelligence()
        {

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

            foreach(MissionContext context in MissionContexts)
            {
                RegionFaction regionFaction = context.Region.RegionFactionMap.Values.First(rf => !rf.PlanetFaction.Faction.IsPlayerFaction && !rf.PlanetFaction.Faction.IsDefaultFaction);
                switch (context.MissionType)
                {
                    case MissionType.Recon:
                        context.Region.IntelligenceLevel += context.Impact;
                        break;
                    case MissionType.Sabotage:
                        SabotageOrder sabotageOrder = (SabotageOrder)context.PlayerSquads.First().Squad.CurrentOrders;
                        int impact = (int)Math.Min(context.Impact, sabotageOrder.TargetSize);
                        switch (sabotageOrder.DefenseType)
                        {
                            case DefenseType.Entrenchment:
                                regionFaction.Entrenchment -= impact;
                                break;
                            case DefenseType.Detection:
                                regionFaction.Detection -= impact;
                                break;
                            case DefenseType.AntiAir:
                                regionFaction.AntiAir -= impact;
                                break;
                        }
                        break;
                }
                regionFaction.Population -= context.EnemiesKilled;
                if(regionFaction.Population < 0)
                {
                    regionFaction.Population = 0;
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

        private void ApplyRegionalFactionActivities(IEnumerable<Planet> planets)
        {
            foreach (Planet planet in planets)
            {
                if (!planet.IsUnderAssault())
                {
                    // TODO we'll eventually need to decide what to do planets that are under assault for the second or third time
                    // currently, they'll just be in whatever state they were left
                    continue;
                }
                foreach (Region region in planet.Regions)
                {
                    foreach (RegionFaction regionFaction in region.RegionFactionMap.Values)
                    {
                        // TODO: generalize this so that Imperial PDFs can build defenses as well
                        if (regionFaction.PlanetFaction.Faction.IsPlayerFaction || regionFaction.PlanetFaction.Faction.IsDefaultFaction)
                        {
                            // Player forces are updated by the player
                            continue;
                        }
                        if (regionFaction.Organization == -1)
                        {
                            // initialize the region faction
                            regionFaction.Organization = 100;
                            regionFaction.Detection = 1;
                            regionFaction.Entrenchment = 1;
                            regionFaction.AntiAir = 1;
                        }
                        if (regionFaction.IsPublic)
                        {
                            ApplyPublicFactionActivities(regionFaction);
                        }
                        else
                        {
                            ApplyHiddenFactionActivities(regionFaction);
                        }
                    }
                }
            }
        }

        private void ApplyHiddenFactionActivities(RegionFaction hiddenFaction)
        {
            // determine if there are public enemy forces in the region
            // determine if there are public enemy forces in adjacent regions
            // determine the forces available

        }

        private void ApplyPublicFactionActivities(RegionFaction publicFaction)
        {
            // determine the forces available
            long organizedTroops = (int)(publicFaction.Population * publicFaction.Organization / 100);
            long disorganizedTroops = publicFaction.Population - organizedTroops;
            int nearbyEnemies = GetAdjacentPlayerAlignedTroops(publicFaction.Region);
            // we need to garrison at least as many enemies as there are nearby
            int garrisonRequirements = nearbyEnemies;
            int structurePoints = (int)(publicFaction.Detection + publicFaction.Entrenchment + publicFaction.AntiAir);
            if (structurePoints == 0 && garrisonRequirements > organizedTroops)
            {
                // if there are no defenses and not enough organized troops to defend
                // remaining troops go into hiding
                publicFaction.IsPublic = false;
            }
            else if (garrisonRequirements < organizedTroops)
            {
                // there are spare troops for other activities
                long buildPointsAvailable = (organizedTroops - garrisonRequirements) / 100;
                // if the organization is below some threshold, need to devote labor to improving that
                int orgInvests = 0, detInvests = 0, entInvests = 0, aaInvests = 0;
                while (buildPointsAvailable > 0)
                {
                    int orgCost;
                    // find the cheapest investment
                    if (publicFaction.Organization + orgInvests == 100)
                    {
                        orgCost = int.MaxValue;
                    }
                    else
                    {
                        orgCost = (int)(Math.Pow(2, orgInvests + 1) * (publicFaction.Population / 100));
                    }
                    int detCost = (int)(Math.Pow(2, publicFaction.Detection + detInvests + 1));
                    int entCost = (int)(Math.Pow(2, publicFaction.Entrenchment + entInvests + 1));
                    int aaCost = (int)(Math.Pow(2, publicFaction.AntiAir + aaInvests + 1));
                    // find the cheapest investment
                    int minCost = Math.Min(orgCost, Math.Min(detCost, Math.Min(entCost, aaCost)));
                    if (minCost <= buildPointsAvailable)
                    {
                        if (minCost == orgCost)
                        {
                            orgInvests++;
                        }
                        else if (minCost == entCost)
                        {
                            entInvests++;

                        }
                        else if (minCost == detCost)
                        {
                            detInvests++;
                        }
                        else if (minCost == aaCost)
                        {
                            aaInvests++;
                        }
                        buildPointsAvailable -= minCost;
                    }
                    else
                    {
                        // no more investments can be made
                        break;
                    }
                }
                publicFaction.Organization += orgInvests;
                publicFaction.Detection += detInvests;
                publicFaction.Entrenchment += entInvests;
                publicFaction.AntiAir += aaInvests;
                publicFaction.Garrison = (int)((organizedTroops % 100) + garrisonRequirements + (buildPointsAvailable * 100));
                if (disorganizedTroops > 0)
                {
                    // some disorganized troops are part of the garrisoning forces by happenstance
                    float unOrganizedPortion = GaussianCalculator.ApproximateNormalCDF((float)RNG.NextRandomZValue()) + 0.5f;
                    publicFaction.Garrison += (int)(disorganizedTroops * unOrganizedPortion);
                }
            }
        }

        private int GetAdjacentPlayerAlignedTroops(Region region)
        {
            int totalTroops = 0;
            foreach (Region adjacentRegion in region.GetSelfAndAdjacentRegions())
            {
                totalTroops += adjacentRegion.RegionFactionMap.Values.Where(rf => rf.PlanetFaction.Faction.IsPlayerFaction || rf.PlanetFaction.Faction.IsDefaultFaction)
                    .SelectMany(rf => rf.LandedSquads)
                    .Sum(s => s.Members.Count);
            }
            return totalTroops;
        }
    }
}
