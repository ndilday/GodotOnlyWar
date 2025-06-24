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
        var orders = new List<Order>();

        // For now, AI launches one major offensive per turn if viable.
        List<PotentialOffensive> potentialOffensives = IdentifyPotentialOffensives(faction, sector);

        if (potentialOffensives.Count > 0)
        {
            PotentialOffensive chosenOffensive = ChooseBestOffensive(potentialOffensives);
            if (chosenOffensive != null)
            {
                GenerateOffensiveOrder(chosenOffensive, orders);
            }
        }
        return orders;
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
        // ... (this method remains largely the same as before, but now calls CalculateRequiredGarrison)
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