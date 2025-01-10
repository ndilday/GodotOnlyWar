using Godot;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public class OrderProcessor
    {
        internal enum Phase
        {
            Infiltrate,
            Act,
            Exfiltrate
        }

        public void ProcessRegionOrders(Region region, IReadOnlyList<Squad> activeSquads)
        {
            // TODO: make player and allied faction work together
            // right now, we're giving each squad its own initative; may switch to one initative per faction/side

            Dictionary<int, int> factionSquadCount = activeSquads.GroupBy(s => s.Faction.Id).ToDictionary(g => g.Key, g => g.Count());
            SortedList<float, int> initativeMap = GenerateInitiativeOrder(activeSquads, factionSquadCount, region);
            Dictionary<int, Phase> squadPhaseCompletedMap = new Dictionary<int, Phase>();
            while(initativeMap.Count > 0)
            {
                var entry = initativeMap.Last();
                // this squad acts next
                Squad squad = activeSquads.First(s => s.Id == entry.Value);
                // if the squad is not currently in the region, it has to move there as its first step
                if (squad.CurrentRegion != region && !squadPhaseCompletedMap.ContainsKey(squad.Id))
                {
                    // check to see if the squad is detected/intercepted on its way to the region
                    HandleSquadMove();
                    // decrement squad initative by 1 and resort the list
                    float newKey = entry.Key - 1.0f;
                    initativeMap.Remove(entry.Key);
                    initativeMap.Add(newKey, squad.Id);
                }
                else if (!squadPhaseCompletedMap.ContainsKey(squad.Id))
                {
                    // squad is in the region and has not acted yet
                }
                else
                {
                    Phase phase = squadPhaseCompletedMap[squad.Id];
                    if(phase == Phase.Infiltrate)
                    {
                        // squad has infiltrated the region and is now acting
                    }
                    else if (phase == Phase.Act)
                    {
                        // squad has acted and is now exfiltrating
                        HandleSquadMove();
                        // squad has left the region
                        initativeMap.Remove(entry.Key);
                        squadPhaseCompletedMap.Remove(squad.Id);
                    }
                }
            }
        }

        private SortedList<float, int> GenerateInitiativeOrder(IReadOnlyList<Squad> squads, Dictionary<int, int> factionSquadCount, Region region)
        {
            // for each cluster, calculate the initiative modifier
            SortedList<float, int> initiativeMap = [];
            foreach (Squad squad in squads)
            {
                float initiative = 0;

                // we probably want faction specific initiative modifiers
                // or possibly some sort of faction+order modifiers, if we think certain factions do better at certain sorts of tactics
                // order-specific modifiers
                if (squad.CurrentRegion == region)
                {
                    // bonus for the action originating in this region
                    initiative += 1.0f;
                }

                // factor in aggression of order
                initiative += (int)squad.CurrentOrders.LevelOfAggression;

                // the larger the number of squads working in the region, the slower their initiative
                initiative -= 1.0f - (1.0f / factionSquadCount[squad.Faction.Id]);

                // add a random wiggle of 0-1 to the initiative
                initiative += (float)RNG.NextGaussianDouble();

                initiativeMap.Add(initiative, squad.Id);
            }
            return initiativeMap;
        }
    
        private void HandleSquadMove()
        {

        }


    }
}
