using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;

namespace OnlyWar.Helpers.Sector
{
    class TurnController
    {
        BattleConfigurationBuilder _battleConfigurationBuilder;

        public TurnController()
        {
            _battleConfigurationBuilder = new BattleConfigurationBuilder(GameDataSingleton.Instance.GameRulesData.PlayerFaction.Id, GameDataSingleton.Instance.GameRulesData.DefaultFaction.Id);
        }
        public void ProcessTurn(Models.Sector sector)
        {
            // for each planet, associate all landed and orbiting squads into the region their Orders target
            foreach(Planet planet in sector.Planets.Values)
            {
                var regionSquadMap = MapSquadsToTargetRegions(planet);
                foreach(var kvp in regionSquadMap)
                {
                    _battleConfigurationBuilder.BuildBattleConfigurations(kvp.Key, kvp.Value);
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
