using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using System.Linq;
using System;

public class FactionStrategyController
{
    private class PotentialOffensive
    {
        public Region TargetRegion { get; set; }
        public RegionFaction TargetFaction { get; set; }
        public List<Region> AttackingRegions { get; set; } = new List<Region>();
        public long AvailableAttackingForce { get; set; }
    }

    private class RegionForceState
    {
        public RegionFaction FactionInfo { get; }
        public long RequiredGarrison { get; }
        public long SpareTroops { get; set; }

        public RegionForceState(RegionFaction factionInfo, long requiredGarrison, long spareTroops)
        {
            FactionInfo = factionInfo;
            RequiredGarrison = requiredGarrison;
            SpareTroops = spareTroops;
        }
    }

    public List<Order> GenerateFactionOrders(Faction faction, Sector sector)
    {
        var allNewOrders = new List<Order>();

        foreach (var planet in sector.Planets.Values)
        {
            var factionRegionsOnPlanet = planet.Regions
                                               .SelectMany(r => r.RegionFactionMap.Values)
                                               .Where(rf => rf.PlanetFaction.Faction == faction && rf.IsPublic)
                                               .ToList();

            if (!factionRegionsOnPlanet.Any()) continue;

            // PRIORITY 1: ASSESS FORCES AND GARRISON NEEDS
            var regionalForceStates = new List<RegionForceState>();
            foreach (var regionFaction in factionRegionsOnPlanet)
            {
                long requiredGarrison = CalculateRequiredGarrison(regionFaction.Region);
                long organizedTroops = (long)(regionFaction.Population * regionFaction.Organization / 100.0f);
                long spareTroops = Math.Max(0, organizedTroops - requiredGarrison);
                regionalForceStates.Add(new RegionForceState(regionFaction, requiredGarrison, spareTroops));
            }

            // PRIORITY 2: PLAN MAJOR OFFENSIVE
            PlanMajorOffensiveOnPlanet(faction, planet, regionalForceStates, allNewOrders);

            // PRIORITY 3: PLAN DEVELOPMENT
            GenerateDevelopmentOrders(regionalForceStates, allNewOrders);

            // PRIORITY 4: PLAN RECON MISSIONS
            PlanReconMissionsOnPlanet(faction, planet, regionalForceStates, allNewOrders);
        }

        return allNewOrders;
    }

    private void PlanMajorOffensiveOnPlanet(Faction faction, Planet planet, List<RegionForceState> regionalForceStates, List<Order> allOrders)
    {
        var potentialOffensives = IdentifyPotentialOffensivesOnPlanet(faction, planet, regionalForceStates);
        if (!potentialOffensives.Any()) return;

        PotentialOffensive chosenOffensive = ChooseBestOffensive(potentialOffensives);
        if (chosenOffensive == null) return;

        long defenderStrength = chosenOffensive.TargetFaction.Garrison + chosenOffensive.TargetFaction.LandedSquads.Sum(s => s.Members.Count);

        // This method ONLY handles major attacks. The force ratio check determines if it proceeds.
        if (chosenOffensive.AvailableAttackingForce <= defenderStrength * 1.5)
        {
            return; // Not strong enough for a major attack, so do nothing in this step.
        }

        int forceBattleValue = (int)(chosenOffensive.AvailableAttackingForce * (0.5 + (RNG.GetLinearDouble() * 0.25)));

        var request = new ForceGenerationRequest { Faction = faction, TargetBattleValue = forceBattleValue, Profile = ForceCompositionProfile.AssaultForce };
        List<Squad> generatedSquads = ForceGenerator.GenerateForce(request);
        if (generatedSquads.Count == 0) return;

        long manpowerCost = generatedSquads.Sum(s => s.Members.Count);
        long totalAvailableForAttack = chosenOffensive.AvailableAttackingForce;
        if (totalAvailableForAttack <= 0) return;

        // Commit troops and deduct from the spare pool
        foreach (var region in chosenOffensive.AttackingRegions)
        {
            var contributingState = regionalForceStates.First(s => s.FactionInfo.Region == region);
            long contribution = (long)(manpowerCost * (contributingState.SpareTroops / (float)totalAvailableForAttack));
            contributingState.SpareTroops -= contribution;
            region.RegionFactionMap[faction.Id].Garrison -= (int)contribution;
        }

        Mission newMission = new Mission(MissionType.Advance, chosenOffensive.TargetFaction, 0);
        Order newOrder = new Order(generatedSquads, Disposition.Mobile, false, true, Aggression.Normal, newMission);
        allOrders.Add(newOrder);
    }

    private void GenerateDevelopmentOrders(List<RegionForceState> regionalForceStates, List<Order> allOrders)
    {
        foreach (var state in regionalForceStates)
        {
            if (state.SpareTroops <= 0) continue;

            long buildPointsAvailable = state.SpareTroops / 100;

            while (buildPointsAvailable > 0)
            {
                int orgCost = (state.FactionInfo.Organization < 100) ? (int)(Math.Pow(2, state.FactionInfo.Organization / 10) * (state.FactionInfo.Population / 10000.0f)) + 1 : int.MaxValue;
                int detCost = (int)Math.Pow(2, state.FactionInfo.Detection + 1);
                int entCost = (int)Math.Pow(2, state.FactionInfo.Entrenchment + 1);
                int aaCost = (int)Math.Pow(2, state.FactionInfo.AntiAir + 1);

                int minCost = Math.Min(orgCost, Math.Min(detCost, Math.Min(entCost, aaCost)));

                if (minCost > buildPointsAvailable || minCost == int.MaxValue) break;

                ConstructionMission mission;
                if (minCost == orgCost) mission = new ConstructionMission(DefenseType.Organization, 1, state.FactionInfo);
                else if (minCost == entCost) mission = new ConstructionMission(DefenseType.Entrenchment, 1, state.FactionInfo);
                else if (minCost == detCost) mission = new ConstructionMission(DefenseType.Detection, 1, state.FactionInfo);
                else mission = new ConstructionMission(DefenseType.AntiAir, 1, state.FactionInfo);

                Order devOrder = new Order(new List<Squad>(), Disposition.DugIn, true, false, Aggression.Avoid, mission);
                allOrders.Add(devOrder);

                buildPointsAvailable -= minCost;
                // Deduct the "manpower cost" of this construction from the available pool for this region
                state.SpareTroops -= minCost * 100;
            }
        }
    }

    private void PlanReconMissionsOnPlanet(Faction faction, Planet planet, List<RegionForceState> regionalForceStates, List<Order> allOrders)
    {
        // This is a new method that runs last, using only the final remaining spare troops.
        foreach (var state in regionalForceStates)
        {
            if (state.SpareTroops <= 0) continue;

            // Check if this region borders an enemy. If not, no need to recon from here.
            var enemyNeighbors = state.FactionInfo.Region.GetAdjacentRegions()
                                    .Where(r => r.RegionFactionMap.Values.Any(rf => AreFactionsEnemies(faction, rf.PlanetFaction.Faction) && rf.IsPublic))
                                    .ToList();

            if (!enemyNeighbors.Any()) continue;

            // Use the remaining spare troops to form a small recon force.
            // BattleValue is roughly 10 per troop.
            int forceBattleValue = (int)state.SpareTroops * 10;
            if (forceBattleValue <= 0) continue;

            var request = new ForceGenerationRequest
            {
                Faction = faction,
                TargetBattleValue = forceBattleValue,
                Profile = ForceCompositionProfile.ScoutPatrol // Use a more appropriate profile
            };

            List<Squad> generatedSquads = ForceGenerator.GenerateForce(request);
            if (generatedSquads.Count == 0) continue;

            // The cost is already "paid" by using up all spare troops, so no further deduction needed.
            // Just create the order. Target the weakest adjacent enemy.
            var target = enemyNeighbors.OrderBy(n => n.RegionFactionMap.Values.First(rf => AreFactionsEnemies(faction, rf.PlanetFaction.Faction)).Garrison)
                                       .First();
            var targetFaction = target.RegionFactionMap.Values.First(rf => AreFactionsEnemies(faction, rf.PlanetFaction.Faction));

            Mission newMission = new Mission(MissionType.Recon, targetFaction, 0);
            Order newOrder = new Order(generatedSquads, Disposition.Mobile, true, false, Aggression.Cautious, newMission);
            allOrders.Add(newOrder);
        }
    }

    // Unchanged helper methods below this point

    private List<PotentialOffensive> IdentifyPotentialOffensivesOnPlanet(Faction attackingFaction, Planet planet, List<RegionForceState> regionalForceStates)
    {
        var potentialOffensives = new List<PotentialOffensive>();
        var allEnemyRegionFactions = planet.Regions.SelectMany(r => r.RegionFactionMap.Values)
                                           .Where(rf => AreFactionsEnemies(attackingFaction, rf.PlanetFaction.Faction) && rf.IsPublic).ToList();

        foreach (var targetFaction in allEnemyRegionFactions)
        {
            var adjacentAttackingRegions = targetFaction.Region.GetAdjacentRegions()
                                                       .Where(r => r.RegionFactionMap.ContainsKey(attackingFaction.Id)).ToList();

            if (adjacentAttackingRegions.Any())
            {
                long availableForce = adjacentAttackingRegions
                    .Select(r => regionalForceStates.FirstOrDefault(s => s.FactionInfo.Region == r)?.SpareTroops ?? 0)
                    .Sum();

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

    private int CalculateRequiredGarrison(Region region)
    {
        int highestThreat = 0;
        foreach (var adjacentRegion in region.GetAdjacentRegions())
        {
            var controllingFaction = region.ControllingFaction;
            if (controllingFaction == null) continue; // No one controls this region, no garrison needed against ghosts.

            int adjacentThreat = adjacentRegion.RegionFactionMap.Values
                .Where(rf => AreFactionsEnemies(controllingFaction.PlanetFaction.Faction, rf.PlanetFaction.Faction))
                .Sum(rf => rf.Garrison + rf.LandedSquads.Sum(s => s.Members.Count));

            if (adjacentThreat > highestThreat) highestThreat = adjacentThreat;
        }
        return highestThreat;
    }

    private PotentialOffensive ChooseBestOffensive(List<PotentialOffensive> offensives)
    {
        return offensives.OrderByDescending(o => o.AvailableAttackingForce / (float)(o.TargetFaction.Garrison + 1)).FirstOrDefault();
    }

    private bool AreFactionsEnemies(Faction f1, Faction f2)
    {
        if (f1 == null || f2 == null) return false;
        bool f1IsImperial = f1.IsPlayerFaction || f1.IsDefaultFaction;
        bool f2IsImperial = f2.IsPlayerFaction || f2.IsDefaultFaction;
        return f1IsImperial != f2IsImperial;
    }
}