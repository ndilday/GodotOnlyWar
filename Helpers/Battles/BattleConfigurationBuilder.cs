using OnlyWar.Builders;
using OnlyWar.Helpers.Battles.Placers;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
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
            // grant each cluser an initative modifier based on faction, order, and size of cluster
            // sort clusters by initiative (plus a wiggle factor, at least to fix ties)
            return null;
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
