using OnlyWar.Models.Soldiers;
using System;

namespace OnlyWar.Helpers.Battles
{
    static public class HitLocationCalculator
    {
        static public HitLocation DetermineHitLocation(BattleSoldier soldier)
        {
            // we're using the "lottery ball" approach to randomness here, where each point of probability
            // for each available body party defines the size of the random linear distribution
            // TODO: factor in cover/body position
            // 
            int roll = RNG.GetIntBelowMax(0, soldier.Soldier.Body.TotalProbabilityMap[soldier.Stance]);
            foreach (HitLocation location in soldier.Soldier.Body.HitLocations)
            {
                int locationChance = location.Template.HitProbabilityMap[(int)soldier.Stance];
                if (roll < locationChance)
                {
                    return location;
                }
                else
                {
                    // this is basically an easy iterative way to figure out which body part on the "chart" the roll matches
                    roll -= locationChance;
                }
            }
            // this should never happen
            throw new InvalidOperationException("Could not determine a hit location");
        }
    }
}
