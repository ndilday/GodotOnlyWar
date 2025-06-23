using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    class TurnController
    {
        public List<MissionContext> MissionContexts { get; private set; }
        public List<Mission> SpecialMissions { get; private set; }

        public TurnController()
        {
            MissionContexts = new List<MissionContext>();
            SpecialMissions = new List<Mission>();
        }

        public void ProcessTurn(Sector sector)
        {
            MissionContexts.Clear();
            SpecialMissions.Clear();

            // --- 1. Strategic Planning Phase ---
            List<Order> allOrdersThisTurn = sector.Orders.Values.ToList();
            ProcessFactionStrategy(sector, allOrdersThisTurn);

            // --- 2. Mission Execution Phase ---
            ProcessMissions(allOrdersThisTurn);

            // --- 3. Planetary Simulation & Resolution Phase ---
            ApplyMissionResults();
            UpdatePlanets(sector.Planets.Values);
            UpdateIntelligence(sector.Planets.Values);
        }

        private void ProcessMissions(IEnumerable<Order> allOrders)
        {
            foreach (Order order in allOrders)
            {
                /*
                if (order.Mission.MissionType == MissionType.Advance)
                {
                    // move these squads to the new region
                    foreach(var squad in order.AssignedSquads)
                    {
                        squad.CurrentRegion.RegionFactionMap[squad.Faction.Id].LandedSquads.Remove(squad);
                        squad.CurrentRegion = order.Mission.RegionFaction.Region;
                        order.Mission.RegionFaction.LandedSquads.Add(squad);
                    }
                }
                // for advance missions where there is no enemy to assault, we don't evaluate the mission after movement
                if(order.Mission.MissionType != MissionType.Advance || !order.Mission.RegionFaction.PlanetFaction.Faction.IsPlayerFaction)
                {
                    List<BattleSquad> playerBattleSquads = order.AssignedSquads.Select(s => new BattleSquad(true, s)).ToList();
                    MissionContext context = new MissionContext(order, playerBattleSquads, new List<BattleSquad>());
                    MissionStepOrchestrator.GetStartingStep(context).ExecuteMissionStep(context, 0, null);
                    MissionContexts.Add(context);
                }
             */
                List<BattleSquad> involvedSquads;
                bool isPlayerOrder = order.AssignedSquads.Any(s => s.Faction.IsPlayerFaction);

                if (isPlayerOrder)
                {
                    involvedSquads = order.AssignedSquads.Select(s => new BattleSquad(true, s)).ToList();
                }
                else
                {
                    // This is an NPC order, generate forces on-the-fly
                    var enemyFaction = order.Mission.RegionFaction.PlanetFaction.Faction;
                    var enemyArmy = TempArmyBuilder.GenerateArmy(enemyFaction); // This needs to be smarter
                                                                                // TODO: Refine TempArmyBuilder to generate a force of a specific size (from order.Mission.MissionSize)
                    involvedSquads = enemyArmy.GetAllSquads().Select(s => new BattleSquad(false, s)).ToList();
                }

                if (involvedSquads.Count == 0) continue;

                MissionContext context = new MissionContext(order, involvedSquads, new List<BattleSquad>());
                MissionStepOrchestrator.GetStartingStep(context).ExecuteMissionStep(context, 0, null);
                MissionContexts.Add(context);
            }
        }

        private void ApplyMissionResults()
        {
            foreach (MissionContext context in MissionContexts)
            {
                // This logic is moved from the old ProcessMissions method
                RegionFaction regionFaction = context.Order.Mission.RegionFaction;
                switch (context.Order.Mission.MissionType)
                {
                    case MissionType.Assassination:
                        // 10 ^ (impact*100) / population = amount of org log
                        // example impact 1 in a population of 1000 = -1 org point
                        int orgLost = (int)(context.Impact * 100 / regionFaction.Population);
                        regionFaction.Organization -= Math.Min(orgLost, regionFaction.Organization);
                        break;
                    case MissionType.Recon:
                        context.Order.Mission.RegionFaction.Region.IntelligenceLevel += context.Impact;
                        break;
                    case MissionType.Sabotage:
                        SabotageMission sabotageMission = (SabotageMission)context.Order.Mission;
                        int impact = (int)Math.Min(context.Impact, sabotageMission.MissionSize);
                        switch (sabotageMission.DefenseType)
                        {
                            case DefenseType.Entrenchment:
                                regionFaction.Entrenchment -= impact;
                                if (regionFaction.Entrenchment < 0)
                                {
                                    regionFaction.Entrenchment = 0;
                                }
                                break;
                            case DefenseType.Detection:
                                regionFaction.Detection -= impact;
                                if (regionFaction.Detection < 0)
                                {
                                    regionFaction.Detection = 0;
                                }
                                break;
                            case DefenseType.AntiAir:
                                regionFaction.AntiAir -= impact;
                                if (regionFaction.AntiAir < 0)
                                {
                                    regionFaction.AntiAir = 0;
                                }
                                break;
                        }
                        break;
                }
                int enemiesKilled = context.EnemiesKilled;
                if (regionFaction.Entrenchment > 0)
                {
                    float ratio = 1.0f - Math.Min(regionFaction.Entrenchment / 10.0f, 1.0f);
                    enemiesKilled = (int)(enemiesKilled * ratio);
                }
                regionFaction.Population -= enemiesKilled;
                if (regionFaction.Population < 0)
                {
                    regionFaction.Population = 0;
                }
            }
        }

        private void UpdatePlanets(IEnumerable<Planet> planets)
        {
            foreach (Planet planet in planets)
            {
                foreach (Region region in planet.Regions)
                {
                    float pdfRatio = region.PlanetaryDefenseForces / (float)region.Population;
                    foreach (RegionFaction regionFaction in region.RegionFactionMap.Values)
                    {
                        if(regionFaction.Population <= 0)
                        {
                            region.RegionFactionMap.Remove(regionFaction.PlanetFaction.Faction.Id);
                        }
                        else
                        {
                            EndOfTurnRegionFactionsUpdate(regionFaction, pdfRatio);
                        }
                    }
                }

                CheckForPlanetaryRevolt(planet);

                foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values)
                {
                    // if the planetFaction no longer has any population on the planet, remove it
                    if (planetFaction.Population <= 0)
                    {
                        planet.PlanetFactionMap.Remove(planetFaction.Faction.Id);
                    }
                    // see if this faction leader is the sort who'd request aid from the player
                    else if (planetFaction.Leader != null)
                    {
                        EndOfTurnLeaderUpdate(planet, planetFaction);
                    }
                }
            }
        }

        private void EndOfTurnRegionFactionsUpdate(RegionFaction regionFaction, float pdfRatio)
        {
            Planet planet = regionFaction.Region.Planet;
            Faction controllingFaction = planet.GetControllingFaction();
            float newPop = 0;
            switch (regionFaction.PlanetFaction.Faction.GrowthType)
            {
                case GrowthType.Logistic:
                    newPop = regionFaction.Population * 0.00015f;
                    break;
                case GrowthType.Conversion:
                    newPop = ConvertPopulation(regionFaction.Region, regionFaction, newPop);
                    if (regionFaction.PlanetFaction.Faction.Id != controllingFaction.Id &&
                        planet.PlanetFactionMap[controllingFaction.Id].Leader != null)
                    {
                        // TODO: see if the governor notices the converted population
                    }
                    break;
                default:
                    newPop = regionFaction.Population * 0.0001f;
                    break;
            }
            if (RNG.GetLinearDouble() < newPop % 1)
            {
                newPop++;
            }
            regionFaction.Population += (int)newPop;
            UpdateRegionFactionForces(regionFaction, pdfRatio, newPop);
        }

        private void UpdateRegionFactionForces(RegionFaction regionFaction, float pdfRatio, float newPop)
        {
            Planet planet = regionFaction.Region.Planet;
            bool isDefaultFaction = regionFaction.PlanetFaction.Faction.IsDefaultFaction;
            bool isPlayerFaction = regionFaction.PlanetFaction.Faction.IsPlayerFaction;

            if (isDefaultFaction || isPlayerFaction || !regionFaction.IsPublic)
            {
                // if the pdf is less than three percent of the population, more people are drafted
                // additionally, secret factions love to infiltrate the PDF
                if (pdfRatio < 0.03f || !regionFaction.IsPublic)
                {
                    regionFaction.Garrison += (int)(newPop * 0.05f);
                }
                else
                {
                    regionFaction.Garrison += (int)(newPop * 0.025f);
                }
            }
            if (planet.IsUnderAssault())
            {
                // TODO: generalize this so that Imperial PDFs can build defenses as well
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
                    ApplyPublicFactionWarActivities(regionFaction);
                }
                else
                {
                    ApplyHiddenFactionWarActivities(regionFaction);
                }
            }
        }

        private void ApplyHiddenFactionWarActivities(RegionFaction hiddenFaction)
        {
            // determine if there are public enemy forces in the region
            // determine if there are public enemy forces in adjacent regions
            // determine the forces available

        }

        private void ApplyPublicFactionWarActivities(RegionFaction publicFaction)
        {
            // determine the forces available
            long organizedTroops = (int)(publicFaction.Population * publicFaction.Organization / 100);
            long disorganizedTroops = publicFaction.Population - organizedTroops;
            int garrisonRequirements = GetAdjacentPlayerAlignedTroops(publicFaction.Region);
            // we need to garrison at least as many enemies as there are nearby
            int structurePoints = publicFaction.Detection + publicFaction.Entrenchment + publicFaction.AntiAir;
            if (structurePoints == 0 && garrisonRequirements > organizedTroops)
            {
                // if there are no defenses and not enough organized troops to defend
                // remaining troops go into hiding
                publicFaction.IsPublic = false;
            }
            else if (garrisonRequirements < organizedTroops)
            {
                // there are spare troops for other activities
                long spareTroops = organizedTroops - garrisonRequirements;
                int defendingTroops;
                Region targetRegion = GetAdjacentPlayerAlignedTarget(publicFaction.Region, spareTroops, out defendingTroops);
                if(targetRegion != null)
                {
                    // send an attack
                    int attackSize = Math.Min((int)((RNG.GetLinearDouble() + 2.0f) * defendingTroops), (int)spareTroops);
                    spareTroops -= attackSize;
                    // 
                }
                long buildPointsAvailable = (spareTroops) / 100;
                int extraTroops = (int)((spareTroops) % 100);
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
                    int detCost = (int)Math.Pow(2, publicFaction.Detection + detInvests + 1);
                    int entCost = (int)Math.Pow(2, publicFaction.Entrenchment + entInvests + 1);
                    int aaCost = (int)Math.Pow(2, publicFaction.AntiAir + aaInvests + 1);
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
                publicFaction.Garrison = (int)(organizedTroops % 100 + garrisonRequirements + buildPointsAvailable * 100);
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
                int regionTroops = adjacentRegion.RegionFactionMap.Values.Where(rf => rf.PlanetFaction.Faction.IsPlayerFaction || rf.PlanetFaction.Faction.IsDefaultFaction)
                    .SelectMany(rf => rf.LandedSquads)
                    .Sum(s => s.Members.Count);
                if (regionTroops > totalTroops)
                {
                    totalTroops = regionTroops;
                }
            }
            return totalTroops;
        }

        private void ProcessFactionStrategy(Sector sector, List<Order> allOrders)
        {
            // Iterate through each faction, except the player's.
            var factions = GameDataSingleton.Instance.GameRulesData.Factions.Where(f => !f.IsPlayerFaction && !f.IsDefaultFaction);
            foreach (Faction faction in factions)
            {
                // Identify all potential offensives for this faction.
                List<PotentialOffensive> potentialOffensives = IdentifyPotentialOffensives(faction, sector);

                if (potentialOffensives.Count > 0)
                {
                    // For now, let's have the AI launch its most promising attack.
                    // We can add more complex logic later (e.g., launching multiple smaller attacks).
                    PotentialOffensive chosenOffensive = ChooseBestOffensive(potentialOffensives);
                    if (chosenOffensive != null)
                    {
                        GenerateOffensiveOrder(chosenOffensive, allOrders);
                    }
                }
            }
        }

        // Placeholder for future faction disposition system.
        private bool AreFactionsEnemies(Faction f1, Faction f2)
        {
            // For now, any non-player, non-default faction is an enemy of the player and default factions.
            // This can be replaced with a diplomacy matrix later.
            bool f1IsImperial = f1.IsPlayerFaction || f1.IsDefaultFaction;
            bool f2IsImperial = f2.IsPlayerFaction || f2.IsDefaultFaction;
            return f1IsImperial != f2IsImperial;
        }

        private List<PotentialOffensive> IdentifyPotentialOffensives(Faction attackingFaction, Sector sector)
        {
            var potentialOffensives = new List<PotentialOffensive>();
            var enemyRegions = sector.Planets.Values.SelectMany(p => p.Regions)
                                            .Where(r => r.RegionFactionMap.Values.Any(rf => AreFactionsEnemies(attackingFaction, rf.PlanetFaction.Faction)))
                                            .ToList();

            foreach (var targetRegion in enemyRegions)
            {
                var offensive = new PotentialOffensive
                {
                    TargetRegion = targetRegion,
                    TargetFaction = targetRegion.RegionFactionMap.Values.First(rf => AreFactionsEnemies(attackingFaction, rf.PlanetFaction.Faction))
                };

                // Find all of this faction's regions that are adjacent to the target
                var adjacentAttackingRegions = targetRegion.GetAdjacentRegions()
                                                           .Where(r => r.RegionFactionMap.ContainsKey(attackingFaction.Id))
                                                           .Select(r => r.RegionFactionMap[attackingFaction.Id])
                                                           .ToList();

                if (adjacentAttackingRegions.Any())
                {
                    offensive.AttackingFactions.AddRange(adjacentAttackingRegions);
                    offensive.CombinedAttackingForce = adjacentAttackingRegions.Sum(rf => rf.Garrison);
                    potentialOffensives.Add(offensive);
                }
            }
            return potentialOffensives;
        }

        private PotentialOffensive ChooseBestOffensive(List<PotentialOffensive> offensives)
        {
            // Simple logic: choose the attack with the best force ratio.
            return offensives.OrderByDescending(o => o.CombinedAttackingForce / (float)(o.TargetFaction.Garrison + 1)).FirstOrDefault();
        }

        private Region GetAdjacentPlayerAlignedTarget(Region region, long spareTroops, out int troopCount)
        {
            Region targetRegion = null;
            int minTroopCount = int.MaxValue;
            foreach (Region adjacentRegion in region.GetSelfAndAdjacentRegions())
            {
                var adjacentRegionFactions = adjacentRegion.RegionFactionMap.Values.Where(rf => rf.PlanetFaction.Faction.IsPlayerFaction || rf.PlanetFaction.Faction.IsDefaultFaction);
                int regionTroops = adjacentRegionFactions
                    .SelectMany(rf => rf.LandedSquads)
                    .Sum(s => s.Members.Count);
                if (regionTroops > 0 && regionTroops < minTroopCount)
                {
                    minTroopCount = regionTroops;
                    targetRegion = adjacentRegion;
                }
            }
            if (minTroopCount * 2 > spareTroops)
            {
                troopCount = 0;
                return null;
            }
            troopCount = minTroopCount;
            return targetRegion;
        }

        private void GenerateOffensiveOrder(PotentialOffensive offensive, List<Order> allOrders)
        {
            // Use the strategic context to decide the mission type.
            long defenderStrength = offensive.TargetFaction.Garrison + offensive.TargetFaction.LandedSquads.Sum(s => s.Members.Count);
            MissionType missionType;

            if (offensive.CombinedAttackingForce > defenderStrength * 1.5)
            {
                missionType = MissionType.Advance;
            }
            else
            {
                // If not overwhelmingly superior, just perform a Recon to gather intel.
                // This can be expanded with Sabotage, etc., later.
                missionType = MissionType.Recon;
            }

            // Commit troops from the garrisons.
            long attackForceSize = (long)(offensive.CombinedAttackingForce * (RNG.GetLinearDouble() * 0.25 + 0.5)); // Commit 50-75% of available garrison
            foreach (var attackingRegionFaction in offensive.AttackingFactions)
            {
                // Deduct proportionally from each attacking region.
                long contribution = (long)(attackForceSize * (attackingRegionFaction.Garrison / (float)offensive.CombinedAttackingForce));
                attackingRegionFaction.Garrison -= (int)contribution;
            }

            // Create the mission and order.
            // Note: The 'MissionSize' here will be used by the execution step to generate the temp army.
            Mission newMission = new Mission(missionType, offensive.TargetFaction, (int)attackForceSize);

            // NPC orders don't need a persistent list of squads. The size is stored in the mission.
            Order newOrder = new Order(new List<Squad>(), Disposition.Mobile, false, true, Aggression.Normal, newMission);

            allOrders.Add(newOrder);
        }

        private void CheckForPlanetaryRevolt(Planet planet)
        {
            Faction controllingFaction = planet.GetControllingFaction();
            PlanetFaction controllingPlanetFaction = planet.PlanetFactionMap[controllingFaction.Id];
            Faction hiddenFactionType = null;
            PlanetFaction hiddenPlanetFaction = null;

            // Find a hidden faction on the planet (assuming only one hidden faction for now)
            foreach (var planetFaction in planet.PlanetFactionMap.Values)
            {
                if (!planetFaction.IsPublic && !planetFaction.Faction.IsDefaultFaction && !planetFaction.Faction.IsPlayerFaction)
                {
                    hiddenFactionType = planetFaction.Faction;
                    hiddenPlanetFaction = planetFaction;
                    break; // Assuming only one hidden faction per planet for now
                }
            }

            // If no hidden faction, no revolt possible
            if (hiddenPlanetFaction != null)
            {
                int hiddenFactionGarrison = 0;
                long hiddenFactionPopulation = 0;
                int controllingFactionGarrison = 0;
                long controllingFactionPopulation = 0;

                foreach (Region region in planet.Regions)
                {
                    foreach (var regionFaction in region.RegionFactionMap.Values)
                    {
                        if (regionFaction.PlanetFaction == controllingPlanetFaction)
                        {
                            controllingFactionGarrison += regionFaction.Garrison;
                            controllingFactionPopulation += regionFaction.Population;
                        }
                        else if (regionFaction.PlanetFaction == hiddenPlanetFaction)
                        {
                            hiddenFactionGarrison += regionFaction.Garrison;
                            hiddenFactionPopulation += regionFaction.Population;
                        }
                    }
                }

                if (hiddenFactionGarrison > controllingFactionGarrison)
                {
                    // Revolt triggers!
                    //context.Log.Add($"{hiddenFactionType.Name} forces trigger planetary revolt on {planet.Name}!");
                    foreach (Region region in planet.Regions)
                    {
                        if (region.RegionFactionMap.ContainsKey(hiddenFactionType.Id))
                        {
                            RegionFaction revoltingRegionFaction = region.RegionFactionMap[hiddenFactionType.Id];
                            revoltingRegionFaction.IsPublic = true;
                            // if there are any regional defenses, the revolters claim half (plus/minus random roll)
                            if (region.RegionFactionMap.ContainsKey(controllingFaction.Id))
                            {
                                RegionFaction controllingRegionFaction = region.RegionFactionMap[controllingFaction.Id];
                                if (controllingRegionFaction.Detection > 0)
                                {
                                    int revoltShare = controllingRegionFaction.Detection / 2;
                                    revoltShare += (int)RNG.NextRandomZValue();
                                    if (revoltShare > controllingRegionFaction.Detection)
                                    {
                                        revoltShare = controllingRegionFaction.Detection;
                                    }
                                    if (revoltShare < 0)
                                    {
                                        revoltShare = 0;
                                    }
                                    controllingRegionFaction.Detection -= revoltShare;
                                    revoltingRegionFaction.Detection += revoltShare;
                                }
                                if (controllingRegionFaction.AntiAir > 0)
                                {
                                    int revoltShare = controllingRegionFaction.AntiAir / 2;
                                    revoltShare += (int)RNG.NextRandomZValue();
                                    if (revoltShare > controllingRegionFaction.AntiAir)
                                    {
                                        revoltShare = controllingRegionFaction.AntiAir;
                                    }
                                    if (revoltShare < 0)
                                    {
                                        revoltShare = 0;
                                    }
                                    controllingRegionFaction.AntiAir -= revoltShare;
                                    revoltingRegionFaction.AntiAir += revoltShare;
                                }
                                if (controllingRegionFaction.Entrenchment > 0)
                                {
                                    int revoltShare = controllingRegionFaction.Entrenchment / 2;
                                    revoltShare += (int)RNG.NextRandomZValue();
                                    if (revoltShare > controllingRegionFaction.Entrenchment)
                                    {
                                        revoltShare = controllingRegionFaction.Entrenchment;
                                    }
                                    if (revoltShare < 0)
                                    {
                                        revoltShare = 0;
                                    }
                                    controllingRegionFaction.Entrenchment -= revoltShare;
                                    revoltingRegionFaction.Entrenchment += revoltShare;
                                }
                                // also negatively impact controlling faction's Organization
                                controllingRegionFaction.Organization = (int)(RNG.GetLinearDouble() * 100);
                            }
                        }

                    }
                    hiddenPlanetFaction.IsPublic = true; // Make PlanetFaction public as well


                }
            }
        }

        private void EndOfTurnLeaderUpdate(Planet planet, PlanetFaction planetFaction)
        {
            // TODO: see if this leader dies
            if (planetFaction.Leader.ActiveRequest != null)
            {
                // see if the request has been fulfilled
                if (planetFaction.Leader.ActiveRequest.IsRequestCompleted())
                {
                    // remove the active request
                    planetFaction.Leader.ActiveRequest = null;
                    // improve leader opinion of player
                    planetFaction.Leader.OpinionOfPlayerForce +=
                        planetFaction.Leader.Appreciation * (1 - planetFaction.Leader.OpinionOfPlayerForce);
                }
                else
                {
                    // decrement the leader's opinion based on the unfulfilled request
                    // the average governor will drop 0.01 opinion per week.
                    planetFaction.Leader.OpinionOfPlayerForce -= 0.005f / planetFaction.Leader.Patience;
                    // TODO: some notion of canceling a request?
                }
            }
            else if (planetFaction.Leader.OpinionOfPlayerForce > 0)
            {
                GenerateRequests(planet, planetFaction);
            }
        }

        private void GenerateRequests(Planet planet, PlanetFaction planetFaction)
        {
            bool found = false;
            bool evidenceFound = false;
            if (planet.PlanetFactionMap.Count > 1)
            {
                // there are other factions on planet
                foreach (PlanetFaction planetOtherFaction in planet.PlanetFactionMap.Values)
                {

                    // make sure this is a different faction and that there isn't already a request about it
                    if (planetOtherFaction.Faction.Id != planetFaction.Faction.Id)
                    {
                        if (!planetOtherFaction.IsPublic)
                        {
                            // see if the leader detects this faction
                            float popRatio = planetOtherFaction.Population / (float)planet.Population;
                            float chance = popRatio * planetFaction.Leader.Investigation;
                            double roll = RNG.GetLinearDouble();
                            if (roll < chance)
                            {
                                found = true;
                                evidenceFound = roll < chance / 10.0;
                                break;
                            }
                        }
                        else
                        {
                            found = true;
                            evidenceFound = true;
                            break;
                        }
                    }
                }
            }
            if (!found)
            {
                // no real threats, see if the leader is paranoid enough to see a threat anyway
                double roll = RNG.GetLinearDouble();
                if (roll < planetFaction.Leader.Paranoia)
                {
                    found = true;
                    evidenceFound = roll < planetFaction.Leader.Paranoia / 10.0;
                }
            }

            if (found)
            {
                // determine if the leader wants to turn this finding into a request
                float chance = planetFaction.Leader.Neediness * planetFaction.Leader.OpinionOfPlayerForce;
                double roll = RNG.GetLinearDouble();
                if (roll < chance)
                {
                    // generate a new request
                    IRequest request = RequestFactory.Instance.GenerateNewRequest(planet, planetFaction.Leader, GameDataSingleton.Instance.Date);
                    planetFaction.Leader.ActiveRequest = request;
                    GameDataSingleton.Instance.Sector.PlayerForce.Requests.Add(request);
                }
            }
        }

        private float ConvertPopulation(Region region, RegionFaction regionFaction, float newPop)
        {
            RegionFaction defaultFaction = region.RegionFactionMap.Values.First(pf => pf.PlanetFaction.Faction.IsDefaultFaction);
            // converting factions always convert one new member per week
            if (defaultFaction?.Population > 0)
            {
                defaultFaction.Population--;
                regionFaction.Population++;
                float pdfChance = (float)defaultFaction.Garrison / defaultFaction.Population;
                if (RNG.GetLinearDouble() < pdfChance)
                {
                    defaultFaction.Garrison--;
                    regionFaction.Garrison++;
                }
                if (regionFaction.Population > 100)
                {
                    // at larger sizes, converting factions
                    // also grow organically 
                    // at a much faster rate than a normal population
                    newPop = regionFaction.Population * 0.002f;
                }
            }

            return newPop;
        }

        private void UpdateIntelligence(IEnumerable<Planet> planets)
        {
            foreach (Planet planet in planets)
            {
                foreach (Region region in planet.Regions)
                {
                    // 25% chance of unexecuted special missions being removed
                    foreach (Mission mission in region.SpecialMissions)
                    {
                        if (RNG.GetIntBelowMax(0, 4) == 0)
                        {
                            // TODO: add to the end of turn log that the intelligence grew stale
                            region.SpecialMissions.Remove(mission);
                        }
                    }
                    if (region.IntelligenceLevel > 0)
                    {
                        foreach (RegionFaction regionFaction in region.RegionFactionMap.Values)
                        {
                            if (regionFaction.PlanetFaction.Faction.IsPlayerFaction || regionFaction.PlanetFaction.Faction.IsDefaultFaction)
                            {
                                continue;
                            }
                            if (regionFaction.IsPublic)
                            {
                                HandlePublicFactionIntelligence(regionFaction);
                            }
                            else
                            {
                                HandleHiddenFactionIntelligence(regionFaction);
                            }
                        }

                        // reduce intelligence level by 25%
                        region.IntelligenceLevel *= 0.75f;
                    }
                }
            }
        }

        public void HandlePublicFactionIntelligence(RegionFaction enemyRegionFaction)
        {
            // see if any intelligence gets spent in exchange for special mission opportunities
            float specMissionChance = (float)Math.Log(enemyRegionFaction.Region.IntelligenceLevel, 2) + 1;
            // subtract one for each special mission already identified
            specMissionChance -= enemyRegionFaction.Region.SpecialMissions.Count;
            for (int i = 0; i < specMissionChance; i++)
            {
                double chance = RNG.NextRandomZValue();
                if (chance >= 2)
                {
                    GenerateAssassinationMission(enemyRegionFaction);
                }
                else if (chance >= 1)
                {
                    // sabotage
                    // add up the amount of entrenchment, detection, and antiair in this region
                    int defenseTotal = enemyRegionFaction.Entrenchment + enemyRegionFaction.Detection + enemyRegionFaction.AntiAir;
                    if (defenseTotal == 0)
                    {
                        GenerateAmbushMission(enemyRegionFaction);
                    }
                    else
                    {
                        GenerateSabotageMission(enemyRegionFaction, defenseTotal);
                    }
                }
                else if (chance >= 0)
                {
                    GenerateAmbushMission(enemyRegionFaction);

                }
            }
        }

        public void HandleHiddenFactionIntelligence(RegionFaction enemyRegionFaction)
        {
            // determine whether the faction can hide among the population
            float popRatio = (float)enemyRegionFaction.Population / (float)enemyRegionFaction.Region.Population;
            float zScore = GaussianCalculator.ApproximateInverseNormalCDF(popRatio);
            zScore += enemyRegionFaction.Region.IntelligenceLevel / 10.0f;
            double chance = RNG.NextRandomZValue();
            if (chance < zScore)
            {
                int size = Math.Max((int)(zScore - chance), 1);
                // found a hidden faction cell
                enemyRegionFaction.Region.SpecialMissions.Add(new Mission(MissionType.Extermination, enemyRegionFaction, size));
            }
        }

        private void GenerateAmbushMission(RegionFaction enemyRegionFaction)
        {
            //make it an ambush, instead
            double maxSize = Math.Log10(enemyRegionFaction.Garrison);
            int size = Math.Min(Math.Max((int)RNG.NextRandomZValue() + 1, 1), (int)maxSize);
            Mission ambush = new Mission(MissionType.Ambush, enemyRegionFaction, size);
            enemyRegionFaction.Region.SpecialMissions.Add(ambush);
            SpecialMissions.Add(ambush);
        }

        private void GenerateSabotageMission(RegionFaction enemyRegionFaction, int defenseTotal)
        {
            int roll = RNG.GetIntBelowMax(0, defenseTotal);
            if (roll <= enemyRegionFaction.Entrenchment)
            {
                // saborage the entrenchments
                int size = Math.Min(Math.Max((int)RNG.NextRandomZValue() + 1, 1), enemyRegionFaction.Entrenchment);
                SabotageMission sabotage = new SabotageMission(0, DefenseType.Entrenchment, size, enemyRegionFaction);
                enemyRegionFaction.Region.SpecialMissions.Add(sabotage);
                SpecialMissions.Add(sabotage);
            }
            else
            {
                roll -= enemyRegionFaction.Entrenchment;
                if (roll <= enemyRegionFaction.Detection)
                {
                    // sabotage the detection
                    int size = Math.Min(Math.Max((int)RNG.NextRandomZValue() + 1, 1), enemyRegionFaction.Detection);
                    SabotageMission sabotage = new SabotageMission(0, DefenseType.Detection, size, enemyRegionFaction);
                    enemyRegionFaction.Region.SpecialMissions.Add(sabotage);
                    SpecialMissions.Add(sabotage);
                }
                else
                {
                    // sabotage the antiair
                    int size = Math.Min(Math.Max((int)RNG.NextRandomZValue() + 1, 1), enemyRegionFaction.AntiAir);
                    SabotageMission sabotage = new SabotageMission(0, DefenseType.AntiAir, size, enemyRegionFaction);
                    enemyRegionFaction.Region.SpecialMissions.Add(sabotage);
                    SpecialMissions.Add(sabotage);
                }
            }
        }

        private void GenerateAssassinationMission(RegionFaction enemyRegionFaction)
        {
            // assassination
            // assume that each degree of magnitude of population increases the "size" of the highest leader
            // for example, with Tyranids, this could be
            // 1-10: Prime
            // 11-100: Broodlord
            // 101-1000: Zoenthope?
            // 1001-10000: Hive Tyrant
            int max = (int)Math.Log10(enemyRegionFaction.Population);
            int size = Math.Min(Math.Max((int)RNG.NextRandomZValue() + 1, 1), max);
            Mission ass = new Mission(0, MissionType.Assassination, enemyRegionFaction, size);
            enemyRegionFaction.Region.SpecialMissions.Add(ass);
            SpecialMissions.Add(ass);
        }

    }
}
