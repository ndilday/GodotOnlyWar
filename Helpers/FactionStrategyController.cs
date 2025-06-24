using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

public class FactionStrategyController
{
    private class PotentialOffensive
    {
        public Region TargetRegion { get; set; }
        public RegionFaction TargetFaction { get; set; }
        public List<Region> AttackingRegions { get; set; } = new List<Region>();
        public long AvailableAttackingForce { get; set; }
    }

    // Public entry point for this controller
    public List<Order> GenerateFactionOrders(Faction faction, Sector sector)
    {
        var allNewOrders = new List<Order>();
        var factionRegions = sector.Planets.Values.SelectMany(p => p.Regions)
                                                   .SelectMany(r => r.RegionFactionMap.Values)
                                                   .Where(rf => rf.PlanetFaction.Faction == faction)
                                                   .ToList();

        // Generate development and garrison orders for each region
        foreach (var regionFaction in factionRegions)
        {
            GenerateDevelopmentOrders(regionFaction, allNewOrders);
        }

        // Generate offensive orders based on strategic situation
        GenerateOffensiveOrders(faction, sector, allNewOrders);

        return allNewOrders;
    }

    private void GenerateOffensiveOrders(Faction faction, Sector sector, List<Order> allOrders)
    {
        // This is adapted from the existing logic to find a single, best offensive.
        List<PotentialOffensive> potentialOffensives = IdentifyPotentialOffensives(faction, sector);

        if (potentialOffensives.Count > 0)
        {
            PotentialOffensive chosenOffensive = ChooseBestOffensive(potentialOffensives);
            if (chosenOffensive != null)
            {
                GenerateOffensiveOrder(chosenOffensive, allOrders);
            }
        }
    }

    private void GenerateDevelopmentOrders(RegionFaction publicFaction, List<Order> allOrders)
    {
        // This is the new home for the logic you described.
        long organizedTroops = (long)(publicFaction.Population * publicFaction.Organization / 100.0f);
        long garrisonRequirements = CalculateRequiredGarrison(publicFaction.Region);

        // If not enough organized troops to even garrison, the faction might go to ground.
        if (publicFaction.Detection + publicFaction.Entrenchment + publicFaction.AntiAir == 0
            && garrisonRequirements > organizedTroops)
        {
            publicFaction.IsPublic = false;
            // In a full implementation, we might issue a "Go To Ground" order.
            return;
        }

        if (garrisonRequirements >= organizedTroops)
        {
            // Not enough troops for development.
            return;
        }

        long spareTroops = organizedTroops - garrisonRequirements;
        long buildPointsAvailable = spareTroops / 100;

        while (buildPointsAvailable > 0)
        {
            int orgCost = (publicFaction.Organization < 100)
                        ? (int)(Math.Pow(2, publicFaction.Organization / 10) * (publicFaction.Population / 10000.0f)) + 1
                        : int.MaxValue;
            int detCost = (int)Math.Pow(2, publicFaction.Detection + 1);
            int entCost = (int)Math.Pow(2, publicFaction.Entrenchment + 1);
            int aaCost = (int)Math.Pow(2, publicFaction.AntiAir + 1);

            int minCost = Math.Min(orgCost, Math.Min(detCost, Math.Min(entCost, aaCost)));

            if (minCost > buildPointsAvailable || minCost == int.MaxValue)
            {
                break; // Can't afford any more improvements.
            }

            ConstructionMission mission = null;
            if (minCost == orgCost)
            {
                mission = new ConstructionMission(DefenseType.Organization, 1, publicFaction);
                publicFaction.Organization++; // Increment here to affect next cost calculation in loop
            }
            else if (minCost == entCost)
            {
                mission = new ConstructionMission(DefenseType.Entrenchment, 1, publicFaction);
                publicFaction.Entrenchment++;
            }
            else if (minCost == detCost)
            {
                mission = new ConstructionMission(DefenseType.Detection, 1, publicFaction);
                publicFaction.Detection++;
            }
            else // aaCost
            {
                mission = new ConstructionMission(DefenseType.AntiAir, 1, publicFaction);
                publicFaction.AntiAir++;
            }

            // Create a squad-less order to represent this background activity
            Order devOrder = new Order(new List<Squad>(), Disposition.DugIn, true, false, Aggression.Avoid, mission);
            allOrders.Add(devOrder);

            buildPointsAvailable -= minCost;
        }
    }

    private void GenerateOffensiveOrder(PotentialOffensive offensive, List<Order> allOrders)
    {
        long defenderStrength = offensive.TargetFaction.Garrison
                              + offensive.TargetFaction.LandedSquads.Sum(s => s.Members.Count);

        MissionType missionType;
        int forceBattleValue;

        if (offensive.AvailableAttackingForce > defenderStrength * 2) // Is a full attack viable?
        {
            missionType = MissionType.Advance;
            // Commit a significant portion of the available (not total) force
            forceBattleValue = (int)(offensive.AvailableAttackingForce * (0.5 + (RNG.GetLinearDouble() * 0.25)));
        }
        else
        {
            // Not strong enough for an attack, pivot to a small recon probe
            missionType = MissionType.Recon;
            forceBattleValue = 200; // A small, fixed value for a recon party
        }

        var request = new ForceGenerationRequest
        {
            Faction = offensive.AttackingRegions.First().RegionFactionMap.Values.First().PlanetFaction.Faction,
            TargetBattleValue = forceBattleValue,
            Profile = ForceCompositionProfile.AssaultForce
        };

        List<Squad> generatedSquads = ForceGenerator.GenerateForce(request);
        if (generatedSquads.Count == 0) return;

        // Commit the manpower from the attacking garrisons
        long manpowerCost = generatedSquads.Sum(s => s.Members.Count);
        foreach (var attackingRegion in offensive.AttackingRegions)
        {
            var attackingFaction = attackingRegion.RegionFactionMap[request.Faction.Id];
            long requiredGarrison = CalculateRequiredGarrison(attackingRegion);
            long availableManpower = attackingFaction.Garrison - requiredGarrison;
            long contribution = (long)(manpowerCost * (availableManpower / (float)offensive.AvailableAttackingForce));
            attackingFaction.Garrison -= (int)contribution;
        }

        Mission newMission = new Mission(missionType, offensive.TargetFaction, 0);
        Order newOrder = new Order(generatedSquads, Disposition.Mobile, false, true, Aggression.Normal, newMission);
        allOrders.Add(newOrder);
    }

    private List<PotentialOffensive> IdentifyPotentialOffensives(Faction attackingFaction, Sector sector)
    {
        var potentialOffensives = new List<PotentialOffensive>();
        var allEnemyRegionFactions = sector.Planets.Values.SelectMany(p => p.Regions)
                                        .SelectMany(r => r.RegionFactionMap.Values)
                                        .Where(rf => AreFactionsEnemies(attackingFaction, rf.PlanetFaction.Faction))
                                        .ToList();

        foreach (var targetFaction in allEnemyRegionFactions)
        {
            var adjacentAttackingRegions = targetFaction.Region.GetAdjacentRegions()
                                                       .Where(r => r.RegionFactionMap.ContainsKey(attackingFaction.Id))
                                                       .ToList();

            if (adjacentAttackingRegions.Any())
            {
                long availableForce = 0;
                foreach (var region in adjacentAttackingRegions)
                {
                    long requiredGarrison = CalculateRequiredGarrison(region);
                    availableForce += region.RegionFactionMap[attackingFaction.Id].Garrison - requiredGarrison;
                }

                if (availableForce > 0)
                {
                    potentialOffensives.Add(new PotentialOffensive
                    {
                        TargetRegion = targetFaction.Region,
                        TargetFaction = targetFaction,
                        AttackingRegions = adjacentAttackingRegions,
                        AvailableAttackingForce = availableForce
                    });
                }
            }
        }
        return potentialOffensives;
    }

    // **ADDRESSES PROBLEM 3: Garrison Commitment**
    private int CalculateRequiredGarrison(Region region)
    {
        int highestThreat = 0;
        foreach (var adjacentRegion in region.GetAdjacentRegions())
        {
            int adjacentThreat = adjacentRegion.RegionFactionMap.Values
                .Where(rf => AreFactionsEnemies(region.ControllingFaction.PlanetFaction.Faction, rf.PlanetFaction.Faction))
                .Sum(rf => rf.Garrison + rf.LandedSquads.Sum(s => s.Members.Count));

            if (adjacentThreat > highestThreat)
            {
                highestThreat = adjacentThreat;
            }
        }
        // AI holds back enough troops to match the single largest adjacent threat.
        return highestThreat;
    }

    private PotentialOffensive ChooseBestOffensive(List<PotentialOffensive> offensives)
    {
        // Simple logic: choose the attack with the best force ratio.
        return offensives.OrderByDescending(o => o.AvailableAttackingForce / (float)(o.TargetFaction.Garrison + 1)).FirstOrDefault();
    }

    private bool AreFactionsEnemies(Faction f1, Faction f2)
    {
        // For now, any non-player, non-default faction is an enemy of the player and default factions.
        // This can be replaced with a diplomacy matrix later.
        bool f1IsImperial = f1.IsPlayerFaction || f1.IsDefaultFaction;
        bool f2IsImperial = f2.IsPlayerFaction || f2.IsDefaultFaction;
        return f1IsImperial != f2IsImperial;
    }

    // ... (ChooseBestOffensive and AreFactionsEnemies methods remain here) ...
}