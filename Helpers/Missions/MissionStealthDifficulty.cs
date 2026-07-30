using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Missions
{
    // What ONE enemy faction is doing to find an intruder on the ground it holds, kept split into the
    // three things it can be doing at once. A faction can watch (sensors and informants), sweep
    // (troops actually out looking), and simply be numerous (bodies in the way) independently, and the
    // three behave completely differently as they scale, so summing them early would throw away the
    // only information a trace needs to explain why a scout was seen.
    public readonly record struct WatchTerms(float Surveillance, float Patrol, float Ambient)
    {
        public float Total => Surveillance + Patrol + Ambient;
    }

    // The individual terms of a stealth check's difficulty, kept separate from their sum so each
    // step can trace what actually made the ground hard to move through. Patrol and Ambient are
    // deliberately NOT collapsed into one "troops" term: they are the same bodies counted under two
    // different behaviours, and the whole point of the model is that the split is what matters.
    public readonly record struct StealthDifficultyTerms(
        float Detection,
        float OwnTroopMod,
        float PatrolMod,
        float AmbientMod,
        float IntelMod,
        int EnemyCount)
    {
        public float Total => Detection + PatrolMod + AmbientMod + OwnTroopMod - IntelMod;
    }

    // The shared model for "how hard is it to move through this region unseen". Every stealth check
    // in the mission system (recon and sabotage stealth, infiltration, exfiltration, ambush
    // positioning, assassination approach) reads it, so the ways a region resists an intruder cannot
    // drift apart between mission types.
    //
    // The model scores detection as an ACT, not as a headcount. The previous version summed
    // log10(deployed strength) across the region, which had three compounding problems:
    //
    //  1. MissionCheck converts difficulty to a z-score with (skill - difficulty) / 5, so one point
    //     of difficulty is 0.2 sigma. An uncapped mass term ranges over 0..8 between a hamlet and a
    //     hive fleet - 1.6 sigma - while every other lever in the model put together spans about 3
    //     points. Mass was not a factor in the check; mass WAS the check, and skill, intel and
    //     surveillance were rounding error next to it.
    //  2. GetDeployedStrength() resolves to Population for a PopulationIsMilitary faction (Tyranids,
    //     cults), so for the factions the player actually infiltrates the term did not mean "how many
    //     troops are hunting me" - it meant "how many creatures live here". A dormant 10^7 hive scored
    //     higher than any garrison in the game while doing nothing at all.
    //  3. Terms were unbounded below, so Log(0) kept reappearing as -infinity at each new call site
    //     and had to be patched with another Max(1, ...) guard.
    //
    // The replacement asks what each enemy faction is DOING. A faction earns difficulty for watching
    // (its own-region intel), for sweeping (the troops it has out on Patrol or Recon), and only a
    // small, hard-capped amount for merely existing in numbers. Everything sits inside log10(1 + x),
    // so every term is >= 0 by construction: an empty region scores exactly 0, nothing can be
    // -infinity or NaN, and the Log(0) bug class is closed off by the shape of the formula rather
    // than by a guard at each call site.
    public static class MissionStealthDifficulty
    {
        // Weight on a faction's own-region intel (unified intel; listening posts, informants, and its
        // own recon of its own ground all feed it). Unchanged from the previous model - intel already
        // sat on a sane 0..~6 scale, so half a difficulty point per point of intel keeps a
        // well-surveilled region worth roughly 0.6 sigma, comparable to a real patrol effort.
        public const float SurveillanceWeight = 0.5f;

        // Weight on the bodies that are present but NOT out searching. Halved relative to the patrol
        // term because standing in a region is a fraction as useful as sweeping it.
        public const float AmbientWeight = 0.5f;

        // The load-bearing constant. Passive density saturates: past a point, more people standing
        // around stop helping to find one scout team, because a stationary crowd does not cover more
        // ground, it covers the same ground more thickly. Without this cap the mass term alone spans
        // 0..4 (0.8 sigma) and re-creates the "population is the whole check" failure the model exists
        // to fix. At 1.5 a dormant 10^7-strong hive and an idle 5,000-strong PDF garrison are worth
        // the same 1.5 as PASSIVE observers - which is right, neither is looking for anyone - and the
        // uncapped patrol term is left as the genuine differentiator between them.
        public const float AmbientSearchCap = 1.5f;

        // Detection is a property of the REGION, not of the mission's chosen target: an intruder is
        // seen by whoever is watching the ground it crosses. A region can hold several enemy factions
        // at once (a public Tyranid incursion sitting on a still-hidden cult), so every term sums over
        // GetDetectingEnemyFactions() - the same set Region.SelectSpotter draws the interceptor from,
        // so difficulty and interceptor always agree on "the enemies present"
        // (Design/MultiFactionRegions.md WI-3).
        public static StealthDifficultyTerms Calculate(
            Region region, int intruderHeadcount, Faction intruder)
        {
            List<RegionFaction> enemies = region.GetDetectingEnemyFactions();
            float surveillance = 0f;
            float patrol = 0f;
            float ambient = 0f;
            foreach (RegionFaction rf in enemies)
            {
                WatchTerms watch = CalculateWatchTerms(rf);
                surveillance += watch.Surveillance;
                patrol += watch.Patrol;
                ambient += watch.Ambient;
            }
            // A larger intruding force leaves more tracks, noise, and silhouettes; the same log10(1+x)
            // shape means a lone operator adds nothing and a company adds about two points.
            float ownTroopMod = Magnitude(intruderHeadcount);
            // The intruder's own knowledge of the region makes it easier to find a stealthy route. This
            // is the only negative term in the model, and it is bounded by the intel scale, so the
            // total can go slightly below zero for a well-briefed force crossing empty ground - which
            // is the intended "this is a formality" case, not a numerical accident.
            float intelMod = intruder == null ? 0f : region.GetFactionRegionIntel(intruder);
            return new StealthDifficultyTerms(
                surveillance, ownTroopMod, patrol, ambient, intelMod, enemies.Count);
        }

        // One faction's total search effort. Exposed so Region.SelectSpotter can weight the spotter
        // roll by exactly the same number that made the region hard to cross: before this, difficulty
        // was weighted by strength-and-intel while the spotter was drawn by intel-then-strength, so
        // the faction that made the crossing hard and the faction that caught you could disagree.
        public static float CalculateWatchScore(RegionFaction rf) => CalculateWatchTerms(rf).Total;

        // The three components of one faction's search effort.
        public static WatchTerms CalculateWatchTerms(RegionFaction rf)
        {
            if (rf == null) return new WatchTerms(0f, 0f, 0f);

            // Sensors, informants, and standing awareness of its own ground.
            float surveillance = SurveillanceWeight * rf.GetOwnRegionIntel();

            // Troops actually out looking. This is the term with no ceiling on it, because sweeping
            // scales the way searching really does: doubling the number of patrols genuinely doubles
            // the ground being turned over. It is also the lever the player and the AI can both move
            // within a turn, which is what makes a region's security a decision rather than a stat.
            long patrolStrength = rf.GetPatrolStrength();
            float patrol = Magnitude(patrolStrength);

            // Everyone else: garrison standing to, workers, the sleeping mass of a hive. They are an
            // obstacle and an audience, not a search. Subtracting the patrollers keeps a body from
            // being counted twice - once at full weight for sweeping and again for standing there.
            long staticStrength = Math.Max(0L, rf.GetDeployedStrength() - patrolStrength);
            float ambient = Math.Min(AmbientSearchCap, AmbientWeight * Magnitude(staticStrength));

            // Attention drawn elsewhere TODAY by a shaping mission - a diversion demonstrating on the
            // border. Drawn from the patrol term first and only then from the ambient term, because
            // responding to a demonstration is the mobile screen's job: dug-in troops stand to in
            // place, and moving them takes a great deal more than bending the screen does. That
            // priority is what prices the feint-then-strike combination correctly, and it needs no
            // player or AI decision - just one number and an order of draw.
            //
            // Surveillance is never divertible: sensors, informants, and standing awareness of your own
            // ground do not turn their heads to watch a demonstration.
            float drawn = Math.Max(0f, rf.CommittedAttention);
            float patrolDrawn = Math.Min(patrol, drawn);
            patrol -= patrolDrawn;
            ambient = Math.Max(0f, ambient - (drawn - patrolDrawn));

            return new WatchTerms(surveillance, patrol, ambient);
        }

        // The shared shape of every magnitude term in the difficulty model: log10(1 + count).
        //
        // The "1 +" is not cosmetic and must not be refactored into a bare Log10. It is what makes
        // every term of the model finite and non-negative by construction: zero maps to exactly 0
        // instead of -infinity, so there is no Max(1, ...) guard to forget at the next call site, and
        // no path by which a difficulty of -infinity turns into a margin of +infinity and hands an
        // intruder a free automatic success. That failure mode was rediscovered and patched in one
        // mission step after another before the shape was fixed here instead.
        public static float Magnitude(long count) =>
            count <= 0L ? 0f : (float)Math.Log10(1.0 + count);

        // Order of magnitude of a troop count, floored at zero, WITHOUT the +1 shift. This is the
        // banding form: PlanetTurnProcessor.GenerateAmbushMission and GenerateAssassinationMission use
        // it to cap a rolled mission size against how large the defender actually is, where the useful
        // property is that 100 troops means band 2 and 1,000,000 means band 6 exactly. The stealth
        // model deliberately does NOT use this - see Magnitude above for why the shifted form is the
        // one that keeps the difficulty terms safe.
        public static float TroopMagnitude(long troops) =>
            (float)Math.Log(Math.Max(1L, troops), 10);
    }
}
