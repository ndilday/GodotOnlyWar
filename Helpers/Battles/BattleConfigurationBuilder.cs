using OnlyWar.Builders;
using OnlyWar.Helpers.Battles.Placers;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles
{
    public class BattleConfigurationBuilder
    {
        int _playerFactionId;
        int _alliedFactionId;
        public BattleConfigurationBuilder(int playerFactionId, int alliedFactionId)
        {
            _playerFactionId = playerFactionId;
            _alliedFactionId = alliedFactionId;
        }
        public IReadOnlyList<BattleConfiguration> BuildBattleConfigurations(Region region, IReadOnlyList<Squad> activeSquads)
        {
            
            // cluster squads by faction, order, and location
            // TODO: make player and allied faction work together
            // TODO: 
            Dictionary<Tuple<Faction, OrderType, Region>, List<Squad>> squadClusterMap = 
                activeSquads
                .GroupBy(squad => new Tuple<Faction, OrderType, Region>(squad.Faction, squad.CurrentOrders.OrderType, squad.CurrentRegion))
                .ToDictionary(group => group.Key, group => group.ToList());
            // grant each cluser an initative modifier based on faction, order, and size of cluster
            // sort clusters by initiative (plus a wiggle factor, at least to fix ties)
            SortedList<float, Tuple<Faction, OrderType, Region>> initativeMap = GenerateInitiativeOrder(squadClusterMap, region);
            foreach(var entry in initativeMap.Reverse())
            {
                // this cluster acts next
            }
            
            return null;
        }

        private SortedList<float, Tuple<Faction, OrderType, Region>> GenerateInitiativeOrder(Dictionary<Tuple<Faction, OrderType, Region>, List<Squad>> squadClusterMap, Region region)
        {
            // for each cluster, calculate the initiative modifier
            SortedList<float, Tuple<Faction, OrderType, Region>> initiativeMap = [];
            foreach (KeyValuePair<Tuple<Faction, OrderType, Region>, List<Squad>> kvp in squadClusterMap)
            {
                float initiative = 0;
                
                // we probably want faction specific initiative modifiers
                // or possibly some sort of faction+order modifiers, if we think certain factions do better at certain sorts of tactics
                // order-specific modifiers
                switch(kvp.Key.Item2)
                {
                    case OrderType.AttackRegion:
                        break;
                    case OrderType.DefendBorder:
                        initiative += 1.0f;
                        break;
                    case OrderType.LandInRegion:
                        initiative += 0.5f;
                        break;
                }
                if(kvp.Key.Item3 == region)
                {
                    // bonus for the action originating in this region
                    initiative += 1.0f;
                }

                // the larger the number of squads working together, the slower their initiative
                initiative -= 1.0f - (1.0f/kvp.Value.Count);

                // add a random wiggle of 0-1 to the initiative
                initiative += (float)RNG.NextGaussianDouble();

                initiativeMap.Add(initiative, kvp.Key);
            }
            return initiativeMap;
        }

        private static Unit GenerateNewArmy(RegionFaction regionFaction, Planet planet)
        {
            int factionId = regionFaction.PlanetFaction.Faction.Id;
            // if we got here, the assaulting force doesn't have an army generated
            // generate an army (and decrement it from the population
            Unit newArmy = TempArmyBuilder.GenerateArmyFromRegionFaction(regionFaction);

            // add unit to faction
            regionFaction.PlanetFaction.Faction.Units.Add(newArmy);

            // add unit to planet
            regionFaction.LandedSquads.AddRange(newArmy.Squads);

            // modify planetFaction based on new unit
            int headcount = newArmy.GetAllMembers().Count();
            float ratio = ((float)regionFaction.PDFMembers) /
                (regionFaction.Population + regionFaction.PDFMembers);
            int pdfHeadcount = (int)(headcount * ratio);
            headcount -= pdfHeadcount;
            regionFaction.PDFMembers -= pdfHeadcount;
            if(regionFaction.PDFMembers < 0)
            {
                headcount -= regionFaction.PDFMembers;
                regionFaction.PDFMembers = 0;
            }
            regionFaction.Population -= headcount;
            if(regionFaction.Population < 0)
            {
                regionFaction.Population = 0;
                // TODO: remove this planetFaction from the planet?
            }
            return newArmy;
        }
    
        private static BattleConfiguration ConstructAnnihilationConfiguration(Region region)
        {
            List<Squad> playerSquads = [];
            List<Squad> opposingSquads = [];
            foreach(RegionFaction regionFaction in region.RegionFactionMap.Values)
            {
                if(regionFaction.PlanetFaction.Faction.IsPlayerFaction)
                {
                    foreach(Squad squad in regionFaction.LandedSquads)
                    {
                        playerSquads.Add(squad);
                    }
                }
                else if(!regionFaction.PlanetFaction.Faction.IsDefaultFaction)
                {
                    foreach(Squad squad in regionFaction.LandedSquads)
                    {
                        opposingSquads.Add(squad);
                    }
                }
            }

            BattleConfiguration config = new BattleConfiguration();
            config.PlayerSquads = CreateBattleSquadList(playerSquads, true);
            config.OpposingSquads = CreateBattleSquadList(opposingSquads, false);
            config.Region = region;
            config.Grid = new BattleGridManager();
            AnnihilationPlacer placer = new AnnihilationPlacer(config.Grid);
            placer.PlaceSquads(config.PlayerSquads, config.OpposingSquads);
            return config;
        }

        private static BattleConfiguration ConstructOpposingAmbushConfiguration(Region region)
        {
            List<Squad> playerSquads = [];
            List<Squad> opposingSquads = [];
            foreach (RegionFaction regionFaction in region.RegionFactionMap.Values)
            {
                if (regionFaction.PlanetFaction.Faction.IsPlayerFaction)
                {
                    foreach (Squad squad in regionFaction.LandedSquads)
                    {
                        playerSquads.Add(squad);
                    }
                }
                else if (!regionFaction.PlanetFaction.Faction.IsDefaultFaction)
                {
                    foreach (Squad squad in regionFaction.LandedSquads)
                    {
                        opposingSquads.Add(squad);
                    }
                }
            }

            BattleConfiguration config = new BattleConfiguration();
            config.PlayerSquads = CreateBattleSquadList(playerSquads, true);
            config.OpposingSquads = CreateBattleSquadList(opposingSquads, false);
            config.Region = region;
            config.Grid = new BattleGridManager();
            AmbushPlacer placer = new AmbushPlacer(config.Grid);
            placer.PlaceSquads(config.PlayerSquads, config.OpposingSquads);
            return config;
        }

        private static List<BattleSquad> CreateBattleSquadList(IReadOnlyList<Squad> squads,
                                                               bool isPlayerSquad)
        {
            List<BattleSquad> battleSquadList = [];
            foreach (Squad squad in squads)
            {
                BattleSquad bs = new BattleSquad(isPlayerSquad, squad);

                battleSquadList.Add(bs);
            }
            return battleSquadList;
        }
    }
}
