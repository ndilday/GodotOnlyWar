using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Missions
{
    // The individual terms of a stealth check's difficulty, kept separate from their sum so each
    // step can trace what actually made the ground hard to move through.
    public readonly record struct StealthDifficultyTerms(
        float Detection,
        float OwnTroopMod,
        float TroopMod,
        float IntelMod,
        int EnemyCount)
    {
        public float Total => Detection + OwnTroopMod + TroopMod - IntelMod;
    }

    // The shared model for "how hard is it to move through this region unseen". Every stealth check
    // in the mission system (recon and sabotage stealth, infiltration, exfiltration) reads it, so
    // the ways a region resists an intruder cannot drift apart between mission types.
    public static class MissionStealthDifficulty
    {
        // Detection is a property of the REGION, not of the mission's chosen target: an intruder is
        // seen by whoever is watching the ground it crosses. A region can hold several enemy factions
        // at once (a public Tyranid incursion sitting on a still-hidden cult), so both the awareness
        // and troop terms sum over GetDetectingEnemyFactions() - the same set Region.SelectSpotter
        // draws the interceptor from, so difficulty and interceptor always agree on "the enemies
        // present" (Design/MultiFactionRegions.md WI-3).
        public static StealthDifficultyTerms Calculate(
            Region region, int intruderHeadcount, Faction intruder)
        {
            List<RegionFaction> enemies = region.GetDetectingEnemyFactions();
            // The defenders' combined awareness of their own ground (unified intel; a patrol sweeping
            // the region raises this directly, so a patrolled region is intrinsically harder to slip
            // through).
            float detection = enemies.Sum(rf => rf.GetOwnRegionIntel()) * 0.5f;
            // every degree of magnitude of troops adds one to the difficulty
            float ownTroopMod = TroopMagnitude(intruderHeadcount);
            // every degree of magnitude of enemy troops fielded in the region adds to the difficulty
            float troopMod = TroopMagnitude(enemies.Sum(rf => rf.GetDeployedStrength()));
            // the intruder's own knowledge of the region makes it easier to find a stealthy route
            float intelMod = intruder == null ? 0f : region.GetFactionRegionIntel(intruder);
            return new StealthDifficultyTerms(
                detection, ownTroopMod, troopMod, intelMod, enemies.Count);
        }

        // Order of magnitude of a troop count, as a difficulty modifier. The Max(1, ...) guard is
        // mandatory: Log(0) is -infinity, which would propagate through the difficulty into a margin
        // of +infinity and make the check trivially succeed. Zero is reachable in normal play -
        // deployed strength is zero for a faction with no organization, and a raw Garrison is always
        // zero for a PopulationIsMilitary horde (Tyranids, cults) whose army IS its population.
        // Callers should feed this GetDeployedStrength(), not Garrison, for the same reason.
        public static float TroopMagnitude(long troops) =>
            (float)Math.Log(Math.Max(1L, troops), 10);
    }
}
