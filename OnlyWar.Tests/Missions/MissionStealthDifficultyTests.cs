using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Missions.Sabotage;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Missions;

// The stealth difficulty model shared by every mission step that has to move through a region unseen.
//
// The model scores detection as an ACT - surveillance, patrols, and a hard-capped allowance for sheer
// density - rather than as a headcount. These tests pin the three things that model has to get right:
//   * the arithmetic itself, against a hand-worked calibration table;
//   * that the same force is meaningfully harder to evade when it is patrolling than when it is idle,
//     which is the entire behavioural point of the redesign;
//   * the two bug classes the old headcount model kept producing - Log(0) = -infinity leaking a
//     +infinity margin into a check, and reading raw Garrison (permanently 0 for a
//     PopulationIsMilitary horde) so a Tyranid- or cult-held region priced as empty ground.
public class MissionStealthDifficultyTests
{
    // --- the shared magnitude shapes ---

    // Every term in the difficulty model runs through log10(1 + x). The "1 +" is what makes zero map
    // to exactly 0 rather than -infinity, so no call site needs its own Max(1, ...) guard and no
    // difficulty can ever come back as -infinity (which the check turns into a +infinity margin and a
    // free automatic success).
    [Theory]
    [InlineData(-5L, 0.0)]
    [InlineData(0L, 0.0)]
    [InlineData(1L, 0.30103)]
    [InlineData(9L, 1.0)]
    [InlineData(999L, 3.0)]
    [InlineData(1_000_000L, 6.0)]
    public void Magnitude_IsLog10OfOnePlusCount_AndNeverNegativeInfinity(long count, double expected)
    {
        float magnitude = MissionStealthDifficulty.Magnitude(count);

        Assert.True(float.IsFinite(magnitude));
        Assert.True(magnitude >= 0f);
        Assert.Equal(expected, magnitude, 4);
    }

    // TroopMagnitude is the unshifted banding form and stays that way: the special-mission generators
    // (PlanetTurnProcessor.GenerateAmbushMission / GenerateAssassinationMission) cap a rolled mission
    // size against it, and there the useful property is that 1,000 troops means band 3 exactly. It
    // keeps its own Max(1, ...) floor because it is not part of the difficulty model.
    [Theory]
    [InlineData(0L, 0.0)]
    [InlineData(1L, 0.0)]
    [InlineData(10L, 1.0)]
    [InlineData(1000L, 3.0)]
    [InlineData(1_000_000L, 6.0)]
    public void TroopMagnitude_StillBandsExactlyForMissionSizing(long troops, double expected)
    {
        float magnitude = MissionStealthDifficulty.TroopMagnitude(troops);

        Assert.True(float.IsFinite(magnitude));
        Assert.Equal(expected, magnitude, 4);
    }

    // --- the calibration table ---
    //
    // Worked by hand from SurveillanceWeight = 0.5, AmbientWeight = 0.5, AmbientSearchCap = 1.5:
    //
    //   idle PDF, 5,000 deployed, intel 2 : 0.5*2 + log10(1+0)     + min(1.5, 0.5*log10(5001)) = 2.50
    //   same PDF with 500 patrolling      : 0.5*2 + log10(1+500)   + min(1.5, 0.5*log10(4501)) = 5.20
    //   dormant hive, 10^7, intel 2       : 0.5*2 + log10(1+0)     + min(1.5, 0.5*log10(10^7)) = 2.50
    //
    // The third line is the one the model exists for. A 10,000,000-strong hive doing nothing scores
    // exactly what a 5,000-strong PDF garrison doing nothing scores, because a stationary crowd does
    // not cover more ground, it covers the same ground more thickly. Under the old sum-of-logs model
    // those two were 7.0 and 3.7 apart on a scale where 5 points is one sigma of skill.

    [Fact]
    public void WatchScore_NoEnemiesPresentAtAll_IsExactlyZero()
    {
        Region region = CreateRegion();

        StealthDifficultyTerms terms = MissionStealthDifficulty.Calculate(
            region, intruderHeadcount: 0, intruder: null);

        Assert.Equal(0, terms.EnemyCount);
        Assert.Equal(0.0, TotalWatchScore(region), 2);
        Assert.Equal(0f, terms.Total);
    }

    [Fact]
    public void WatchScore_IdleGarrisonOfFiveThousandWithIntelTwo_IsTwoPointFive()
    {
        Region region = CreateRegion();
        AddGarrisonFaction(region, CreateFaction(20, "PDF"), garrison: 5_000, intel: 2f);

        Assert.Equal(2.50, TotalWatchScore(region), 2);
    }

    [Fact]
    public void WatchScore_FiveHundredOfThatGarrisonPatrolling_IsFivePointTwo()
    {
        Region region = CreateRegion();
        RegionFaction pdf = AddGarrisonFaction(
            region, CreateFaction(20, "PDF"), garrison: 5_000, intel: 2f);
        SendOnMission(pdf, MissionType.Patrol, battleValue: 500);

        Assert.Equal(500L, pdf.GetPatrolStrength());
        Assert.Equal(5.20, TotalWatchScore(region), 2);
    }

    [Fact]
    public void WatchScore_DormantTenMillionStrongHiveWithIntelTwo_IsAlsoTwoPointFive()
    {
        Region region = CreateRegion();
        AddHordeFaction(region, CreateFaction(20, "Swarm"), population: 10_000_000, intel: 2f);

        Assert.Equal(2.50, TotalWatchScore(region), 2);
    }

    // --- the behaviour the table is there to protect ---

    // The whole point of splitting patrol from ambient: the same troops, doing something different,
    // have to price differently. If this ever collapses to equality the model has silently reverted to
    // a headcount.
    [Fact]
    public void Calculate_PatrollingForce_IsStrictlyHarderToEvadeThanTheIdenticalIdleForce()
    {
        Region idleRegion = CreateRegion();
        AddGarrisonFaction(idleRegion, CreateFaction(20, "PDF"), garrison: 5_000, intel: 2f);

        Region patrolledRegion = CreateRegion();
        RegionFaction patrolling = AddGarrisonFaction(
            patrolledRegion, CreateFaction(20, "PDF"), garrison: 5_000, intel: 2f);
        SendOnMission(patrolling, MissionType.Patrol, battleValue: 500);

        float idle = MissionStealthDifficulty
            .Calculate(idleRegion, intruderHeadcount: 5, intruder: null).Total;
        float patrolled = MissionStealthDifficulty
            .Calculate(patrolledRegion, intruderHeadcount: 5, intruder: null).Total;

        Assert.True(patrolled > idle);
        // And by a margin that matters: MissionCheck divides by 5 to get a z-score, so the 2.7-point
        // gap here is over half a sigma of skill - a real decision, not a rounding difference.
        Assert.Equal(2.70, patrolled - idle, 2);
    }

    // Recon is a search order too (cover ground, report what you find), so it feeds the patrol term.
    // Anything else - here a squad dug in and fortifying - is occupied by its own mission and counts
    // as static: an obstacle in the region, not a sweep of it.
    [Theory]
    [InlineData(MissionType.Patrol, true)]
    [InlineData(MissionType.Recon, true)]
    [InlineData(MissionType.Fortify, false)]
    [InlineData(MissionType.Advance, false)]
    [InlineData(MissionType.LastStand, false)]
    public void GetPatrolStrength_CountsOnlySquadsActuallyOutSearching(
        MissionType missionType, bool counted)
    {
        Region region = CreateRegion();
        RegionFaction pdf = AddGarrisonFaction(
            region, CreateFaction(20, "PDF"), garrison: 5_000, intel: 0f);
        SendOnMission(pdf, missionType, battleValue: 40);

        Assert.Equal(counted ? 40L : 0L, pdf.GetPatrolStrength());
        Assert.Equal(counted, MissionStealthDifficulty.CalculateWatchTerms(pdf).Patrol > 0f);
    }

    // A squad standing in the region with no orders at all is not searching either, and must not
    // dereference its way to a NullReferenceException on the way to that answer.
    [Fact]
    public void GetPatrolStrength_SquadWithNoOrders_CountsAsStatic()
    {
        Region region = CreateRegion();
        RegionFaction pdf = AddGarrisonFaction(
            region, CreateFaction(20, "PDF"), garrison: 5_000, intel: 0f);
        pdf.LandedSquads.Add(CreateSquadOfSize("Idle Squad", 10));

        Assert.Equal(0L, pdf.GetPatrolStrength());
    }

    // The cap has to actually bind, or the model degenerates back into "population is the check".
    // Ten times the bodies, none of them searching, buys exactly nothing.
    [Fact]
    public void Calculate_AmbientTerm_SaturatesAtTheCapNoMatterHowLargeTheHorde()
    {
        Region small = CreateRegion();
        AddHordeFaction(small, CreateFaction(20, "Swarm"), population: 1_000_000, intel: 0f);
        Region tenTimesLarger = CreateRegion();
        AddHordeFaction(tenTimesLarger, CreateFaction(20, "Swarm"), population: 10_000_000, intel: 0f);

        StealthDifficultyTerms smallTerms = MissionStealthDifficulty.Calculate(
            small, intruderHeadcount: 5, intruder: null);
        StealthDifficultyTerms largeTerms = MissionStealthDifficulty.Calculate(
            tenTimesLarger, intruderHeadcount: 5, intruder: null);

        Assert.Equal(MissionStealthDifficulty.AmbientSearchCap, smallTerms.AmbientMod);
        Assert.Equal(MissionStealthDifficulty.AmbientSearchCap, largeTerms.AmbientMod);
        Assert.Equal(smallTerms.Total, largeTerms.Total);
    }

    // Below the cap the ambient term still tracks the numbers present, so a thinly-held region really
    // is easier to cross than a densely-held one. The saturation is a ceiling, not a flat rate.
    [Fact]
    public void Calculate_AmbientTerm_TracksDensityUntilItReachesTheCap()
    {
        Region thin = CreateRegion();
        AddHordeFaction(thin, CreateFaction(20, "Swarm"), population: 100, intel: 0f);
        Region thick = CreateRegion();
        AddHordeFaction(thick, CreateFaction(20, "Swarm"), population: 10_000, intel: 0f);

        float thinAmbient = MissionStealthDifficulty
            .Calculate(thin, intruderHeadcount: 5, intruder: null).AmbientMod;
        float thickAmbient = MissionStealthDifficulty
            .Calculate(thick, intruderHeadcount: 5, intruder: null).AmbientMod;

        // 0.5 * log10(101) and 0.5 * log10(10001).
        Assert.Equal(1.0022, thinAmbient, 4);
        Assert.Equal(1.5, thickAmbient, 4);
        Assert.True(thinAmbient < thickAmbient);
    }

    // The cap is applied per faction, not to the region's total. Two hordes sitting on the same ground
    // are two separate crowds with two separate sets of eyes, and the aggregation must not quietly
    // rescue the intruder by clamping their combined presence to a single faction's ceiling.
    [Fact]
    public void Calculate_AmbientCap_AppliesPerFactionNotToTheRegionTotal()
    {
        Region region = CreateRegion();
        AddHordeFaction(region, CreateFaction(20, "Swarm"), population: 1_000_000, intel: 0f);
        AddHordeFaction(region, CreateFaction(21, "Cult"), population: 1_000_000, intel: 0f);

        StealthDifficultyTerms terms = MissionStealthDifficulty.Calculate(
            region, intruderHeadcount: 1, intruder: null);

        Assert.Equal(2, terms.EnemyCount);
        Assert.Equal(2f * MissionStealthDifficulty.AmbientSearchCap, terms.AmbientMod);
    }

    // --- the guarantees the shape of the formula is supposed to give for free ---

    [Fact]
    public void Calculate_EmptyRegion_IsExactlyZeroWithEveryTermFinite()
    {
        Region region = CreateRegion();

        StealthDifficultyTerms terms = MissionStealthDifficulty.Calculate(
            region, intruderHeadcount: 0, intruder: null);

        Assert.Equal(0f, terms.Detection);
        Assert.Equal(0f, terms.PatrolMod);
        Assert.Equal(0f, terms.AmbientMod);
        Assert.Equal(0f, terms.OwnTroopMod);
        Assert.Equal(0f, terms.IntelMod);
        Assert.Equal(0f, terms.Total);
        Assert.All(
            new[] { terms.Detection, terms.PatrolMod, terms.AmbientMod, terms.OwnTroopMod, terms.Total },
            term => Assert.True(float.IsFinite(term)));
    }

    // Regression, re-expressed against the new terms. Present factions with population but no
    // organization, so deployed strength is 0 across the whole region. Under a bare Log10 that is
    // -infinity, and the margin that comes back out of the check is +infinity: the intruder slips in
    // for free no matter how badly it rolls.
    [Fact]
    public void Calculate_NoDeployableTroopsAnywhere_LeavesAFiniteDifficulty()
    {
        Region region = CreateRegion();
        AddEnemy(region, CreateFaction(20, "Alpha"), population: 1_000, organization: 0, intel: 0f);
        AddEnemy(region, CreateFaction(21, "Beta"), population: 1_000, organization: 0, intel: 0f);

        StealthDifficultyTerms terms = MissionStealthDifficulty.Calculate(
            region, intruderHeadcount: 5, intruder: null);

        Assert.Equal(0f, terms.AmbientMod);
        Assert.Equal(0f, terms.PatrolMod);
        Assert.True(float.IsFinite(terms.Total));
    }

    // Regression, re-expressed. The intruder's own headcount goes through the same log, and a force
    // can be emptied of able soldiers mid-mission (the check then auto-fails downstream). Log(0) there
    // would flip the difficulty to -infinity and hand that dead force a guaranteed success on the way
    // out.
    [Fact]
    public void Calculate_EmptyIntruderForce_LeavesAFiniteDifficulty()
    {
        Region region = CreateRegion();
        AddHordeFaction(region, CreateFaction(20, "Swarm"), population: 10_000, intel: 0f);

        StealthDifficultyTerms terms = MissionStealthDifficulty.Calculate(
            region, intruderHeadcount: 0, intruder: null);

        Assert.Equal(0f, terms.OwnTroopMod);
        Assert.True(float.IsFinite(terms.Total));
    }

    // Regression, re-expressed. A PopulationIsMilitary faction's army IS its population; it carries no
    // Garrison at all. The presence terms must come from deployed strength, or infiltrating a
    // Tyranid-held region faces no troop difficulty whatsoever. The size below sits under the ambient
    // cap on purpose, so the assertion proves the Population actually drove the number rather than the
    // ceiling doing it for us.
    [Fact]
    public void Calculate_ZeroGarrisonHorde_IsMeasuredByItsPopulation()
    {
        Region region = CreateRegion();
        RegionFaction horde = AddHordeFaction(
            region, CreateFaction(20, "Swarm"), population: 100, intel: 0f);

        StealthDifficultyTerms terms = MissionStealthDifficulty.Calculate(
            region, intruderHeadcount: 1, intruder: null);

        Assert.Equal(0, horde.Garrison);
        Assert.Equal(100, horde.GetDeployedStrength());
        // 0.5 * log10(101), well below the 1.5 cap.
        Assert.Equal(1.0022, terms.AmbientMod, 4);
    }

    // ...and the same horde's patrols come out of that same population, so a hunting hive fleet is a
    // genuinely different proposition from a dormant one even though both carry a zero Garrison.
    [Fact]
    public void Calculate_HuntingHorde_OutscoresTheIdenticalDormantHorde()
    {
        Region region = CreateRegion();
        RegionFaction horde = AddHordeFaction(
            region, CreateFaction(20, "Swarm"), population: 1_000_000, intel: 0f);
        float dormant = MissionStealthDifficulty.CalculateWatchScore(horde);

        SendOnMission(horde, MissionType.Patrol, battleValue: 200);
        float hunting = MissionStealthDifficulty.CalculateWatchScore(horde);

        Assert.Equal(MissionStealthDifficulty.AmbientSearchCap, dormant);
        // log10(201) on top of an ambient term still pinned at the cap.
        Assert.Equal(3.8032, hunting, 4);
    }

    [Fact]
    public void Calculate_DetectionSumsEveryEnemyFactionsAwareness()
    {
        // A hidden cult watching the ground makes the region harder to cross even when the mission is
        // aimed at the horde sitting on top of it.
        Region region = CreateRegion();
        AddHordeFaction(region, CreateFaction(20, "Swarm"), population: 100, intel: 2f);
        AddHordeFaction(region, CreateFaction(21, "Cult"), population: 100, intel: 4f);

        StealthDifficultyTerms terms = MissionStealthDifficulty.Calculate(
            region, intruderHeadcount: 1, intruder: null);

        Assert.Equal(2, terms.EnemyCount);
        Assert.Equal(3.0, terms.Detection, 4);
    }

    [Fact]
    public void Calculate_IgnoresFactionsWithNeitherTroopsNorAwareness()
    {
        // A faction that has been wiped out of the region is not watching it. It must not be counted
        // as a detecting enemy, and (with nothing left to sum) the total must still be finite.
        Region region = CreateRegion();
        AddEnemy(region, CreateFaction(20, "Ghosts"), population: 0, organization: 100, intel: 0f);

        StealthDifficultyTerms terms = MissionStealthDifficulty.Calculate(
            region, intruderHeadcount: 4, intruder: null);

        Assert.Equal(0, terms.EnemyCount);
        Assert.Equal(0f, terms.AmbientMod);
        Assert.True(float.IsFinite(terms.Total));
    }

    // --- the spotter roll reads the same number the difficulty did ---

    // SelectSpotter used to weight by intel, falling back to strength only when nobody had intel, while
    // the difficulty was built from both together. The faction that made the region hard to cross and
    // the faction that caught you could therefore be different factions - and the interceptor the
    // player then fought was raised from the wrong order of battle. Both sides now read WatchScore.
    [Fact]
    public void SelectSpotter_IsWeightedByWatchScore_SoThePatrollingFactionCatchesYouMostOften()
    {
        Region region = CreateRegion();
        RegionFaction hunters = AddGarrisonFaction(
            region, CreateFaction(20, "Hunters"), garrison: 1_000, intel: 0f);
        SendOnMission(hunters, MissionType.Patrol, battleValue: 100);
        RegionFaction idlers = AddGarrisonFaction(
            region, CreateFaction(21, "Idlers"), garrison: 1_000, intel: 0f);

        float hunterScore = MissionStealthDifficulty.CalculateWatchScore(hunters);
        float idlerScore = MissionStealthDifficulty.CalculateWatchScore(idlers);
        Assert.True(hunterScore > idlerScore * 2f);

        (int hunterHits, int idlerHits) = TallySpotters(
            region, hunters, idlers, 4000, new SeededRNG(1234));

        double observedShare = hunterHits / (double)(hunterHits + idlerHits);
        double expectedShare = hunterScore / (hunterScore + idlerScore);
        Assert.True(hunterHits > idlerHits);
        Assert.InRange(observedShare, expectedShare - 0.03, expectedShare + 0.03);
    }

    // A faction with eyes on the ground but nothing fielded is still a plausible spotter, and one with
    // neither eyes nor anything fielded is not - the weighting has to keep both of those true.
    [Fact]
    public void SelectSpotter_SurveillanceOnlyFaction_StillOutscoresAnUnseeingCrowd()
    {
        Region region = CreateRegion();
        RegionFaction informants = AddEnemy(
            region, CreateFaction(20, "Informants"), population: 500, organization: 0, intel: 6f);
        RegionFaction crowd = AddHordeFaction(
            region, CreateFaction(21, "Crowd"), population: 10_000_000, intel: 0f);

        Assert.Equal(3.0, MissionStealthDifficulty.CalculateWatchScore(informants), 4);
        Assert.Equal(1.5, MissionStealthDifficulty.CalculateWatchScore(crowd), 4);

        (int informantHits, int crowdHits) = TallySpotters(
            region, informants, crowd, 4000, new SeededRNG(99));

        Assert.True(informantHits > crowdHits);
    }

    [Fact]
    public void SelectSpotter_NoEnemyFactionPresent_ReturnsNull()
    {
        // The caller (ReconStealthMissionStep) falls back to the mission's target when this is null,
        // so the contract has to survive the reweighting.
        Region region = CreateRegion();

        Assert.Null(region.SelectSpotter(new SeededRNG(7)));
    }

    [Fact]
    public void SelectSpotter_EveryWatchScoreZero_ReturnsAPresentEnemyWithoutDividingByZero()
    {
        // Present factions (they have population) but neither eyes nor deployable troops
        // (organization 0 => deployed strength 0), so every WatchScore is 0 and there is nothing to
        // weight by. The fallback must still name a present enemy rather than throw.
        Region region = CreateRegion();
        AddEnemy(region, CreateFaction(20, "Alpha"), population: 1_000, organization: 0, intel: 0f);
        AddEnemy(region, CreateFaction(21, "Beta"), population: 1_000, organization: 0, intel: 0f);

        RegionFaction spotter = region.SelectSpotter(new SeededRNG(7));

        Assert.NotNull(spotter);
        Assert.Contains(spotter, region.RegionFactionMap.Values);
    }

    // --- the sabotage steps, end to end ---

    // SabotageStealthMissionStep read Log(Garrison), so against a horde it computed a difficulty of
    // -infinity and every stealth roll came back +infinity: the force walked in unseen every single
    // day regardless of how bad its scouts were. Against ground that is actually being searched - a
    // watching, patrolling defender, which is what the model now asks about - an untrained squad gets
    // caught. (Under the new model the horde's raw size is deliberately NOT enough on its own; see
    // WatchScore_DormantTenMillionStrongHiveWithIntelTwo_IsAlsoTwoPointFive.)
    [Fact]
    public void SabotageStealth_UntrainedForceAgainstASearchedRegion_IsDetected()
    {
        // 60 battle value is 30 patrollers, which is plenty to make the region count as searched. It used
        // to be 250 (125 men): harmless while interception conjured its own force, but since
        // DetectedMissionStep began intercepting with the squads a region actually has, this fixture's
        // patrol IS the intercepting force, and a 125-man squad spent ~14 seconds grinding down one
        // saboteur.
        //
        // TWO STEPS, NOT RunToCompletion (2026-08-09). Detection is what this test is named for and
        // it is fully decided once DetectedMissionStep declares the interception, two steps in.
        // Running to completion fought a battle per mission day -- one saboteur against thirty
        // patrollers, about a minute a run -- none of which bears on whether the region spotted
        // him. See AssassinateStealth_UntrainedForceAgainstASearchedRegion_IsDetected for the
        // fuller note.
        MissionContext context = CreateSabotageContext(
            hordePopulation: 100_000_000, trained: false, defenderIntel: 6f,
            defenderPatrolBattleValue: 60);
        MissionStepDriver driver = new(
            CreateExecution(context), new SabotageStealthMissionStep());

        driver.AdvanceOneStep();  // stealth check fails -> DetectedMissionStep
        driver.AdvanceOneStep();  // interception declared -> the engagement, left unrun

        Assert.Contains(context.Log, line => line.Contains("detected and intercepted"));
        Assert.DoesNotContain(context.Log, line => line.Contains("plants explosives"));
    }

    // PerformSabotageMissionStep had the same bug in its Tactics check, one step further in: with
    // Log10(0) = -infinity the leader's margin was +infinity and Impact accumulated infinity, which
    // then poisons every mission-outcome number downstream of it.
    [Fact]
    public void PerformSabotage_AgainstAZeroGarrisonHorde_AccruesFiniteImpact()
    {
        MissionContext context = CreateSabotageContext(hordePopulation: 1_000_000, trained: true);

        new MissionStepDriver(CreateExecution(context), new SabotageStealthMissionStep()).RunToCompletion();

        Assert.Single(context.Log, l => l.Contains("plants explosives"));
        Assert.True(float.IsFinite(context.Impact));
        Assert.True(context.Impact > 0f);
    }

    // ...and the horde's numbers have to actually matter to the Tactics check that demolishes its
    // works. That check is target-anchored and deliberately stays off the search model (the guard
    // around an installation is the guard whether it is sweeping the hills or not), so the gap between
    // a 100-strong and a 1,000,000-strong defender is still the four orders of magnitude between them.
    // FixedRNG rolls z = 0, so the successful strike's margin is exactly
    // (skill - difficulty) / 5. Under the old raw-Garrison read both were 0 and the two runs were
    // identical.
    [Fact]
    public void PerformSabotage_LargerHorde_IsHarderInProportionToItsMagnitude()
    {
        MissionContext small = CreateSabotageContext(hordePopulation: 100, trained: true);
        MissionContext large = CreateSabotageContext(hordePopulation: 1_000_000, trained: true);

        new MissionStepDriver(CreateExecution(small), new SabotageStealthMissionStep()).RunToCompletion();
        new MissionStepDriver(CreateExecution(large), new SabotageStealthMissionStep()).RunToCompletion();

        float expectedGap = (MissionStealthDifficulty.Magnitude(1_000_000)
            - MissionStealthDifficulty.Magnitude(100)) / 5f;
        Assert.True(small.Impact > large.Impact);
        Assert.Equal(expectedGap, small.Impact - large.Impact, 3);
    }

    // --- fixtures ---

    private static double TotalWatchScore(Region region) =>
        region.GetDetectingEnemyFactions()
            .Sum(rf => (double)MissionStealthDifficulty.CalculateWatchScore(rf));

    private static (int first, int second) TallySpotters(
        Region region, RegionFaction first, RegionFaction second, int iterations, IRNG random)
    {
        int firstHits = 0;
        int secondHits = 0;
        for (int i = 0; i < iterations; i++)
        {
            RegionFaction spotter = region.SelectSpotter(random);
            if (spotter == first) firstHits++;
            else if (spotter == second) secondHits++;
        }
        return (firstHits, secondHits);
    }

    // A sabotage force in the region it already holds, so there is no infiltration or exfiltration
    // to muddy the day count. The defender is a PopulationIsMilitary horde: its whole army sits in
    // Population and its Garrison is zero, which is the case the old code could not price. The
    // defender is dormant unless a test asks for intel and patrollers, because under the search model
    // "dormant" is now a genuinely different situation from "large".
    private static MissionContext CreateSabotageContext(
        long hordePopulation,
        bool trained,
        float defenderIntel = 0f,
        int defenderPatrolBattleValue = 0)
    {
        Planet planet = new(1, "Test Planet", new Coordinate(0, 0), 1, null, 0, 0);
        Region region = new(1, planet, 0, "Target Region", new RegionCoordinate(0, 0), 0);
        planet.Regions[0] = region;
        Faction horde = CreateFaction(20, "Swarm");
        PlanetFaction planetFaction = new(horde);
        RegionFaction target = new(planetFaction, region)
        {
            Population = hordePopulation
        };
        planetFaction.SetRegionIntel(region, defenderIntel);
        region.RegionFactionMap[horde.Id] = target;
        if (defenderPatrolBattleValue > 0)
        {
            SendOnMission(target, MissionType.Patrol, defenderPatrolBattleValue);
        }

        Squad squad = TestModelFactory.CreateSquad(
            "Saboteur Squad",
            CreateSaboteur(TestModelFactory.SergeantTemplate, "Saboteur Sergeant", trained),
            CreateSaboteur(TestModelFactory.MarineTemplate, "Saboteur", trained));
        squad.CurrentRegion = region;
        Order order = new(
            [squad],
            isQuiet: true,
            isActivelyEngaging: false,
            levelOfAggression: Aggression.Normal,
            mission: new Mission(MissionType.Sabotage, target, missionSize: 0));

        return new MissionContext(order, [new BattleSquad(true, squad)], []);
    }

    // Trained saboteurs clear every check in the loop, so the mission runs its full day budget and
    // the test can measure Impact. Untrained ones fall back to the unskilled attribute value and are
    // caught, which is what makes the difficulty visible at all.
    private static Soldier CreateSaboteur(SoldierTemplate template, string name, bool trained) =>
        trained
            ? TestModelFactory.CreateSoldier(
                template,
                name,
                skills: [new Skill(TestSkills.Stealth, 256), new Skill(TestSkills.Tactics, 256)])
            : TestModelFactory.CreateSoldier(template, name);

    private static MissionExecutionContext CreateExecution(MissionContext context) =>
        TestExecutionContextFactory.CreateMission(context, new FixedRNG());

    // Every soldier TestModelFactory builds carries battleValue: 2, so a squad's battle value is
    // twice its headcount. Named here because the patrol fixtures are specified in BATTLE VALUE - the
    // currency GetDeployedStrength and GetPatrolStrength are both denominated in - and the conversion
    // to bodies is an artifact of the fixture, not of the model.
    private const int TestSoldierBattleValue = 2;

    // Puts a body of this faction's own troops into the region under an order of the given type,
    // sized to a requested BATTLE VALUE rather than a headcount. GetPatrolStrength returns battle
    // value so that it can be subtracted from GetDeployedStrength, which is also battle value; a
    // fixture written in bodies would be testing a different quantity than the model consumes.
    // --- committed attention (a diversion pulling the screen aside) ---
    //
    // These replace the two FactionStrategyController tests that covered PerceivedThreatBonus and
    // ProvocationLevel. Under those, a feint was a purely strategic effect that inflated the garrison
    // the enemy planned to hold and did nothing at all to the search effort an infiltrator faced. The
    // effect now lands here instead, on who is looking where today.

    // Attention comes out of the patrol term first: responding to a demonstration is the mobile
    // screen's job, so a light feint bends the screen and leaves the dug-in troops where they are.
    [Fact]
    public void CommittedAttention_DrawsFromThePatrolTermFirst()
    {
        Region region = CreateRegion();
        RegionFaction defender = AddHordeFaction(region, CreateFaction(20, "Swarm"), 10_000, intel: 0f);
        SendOnMission(defender, MissionType.Patrol, 1_000);

        WatchTerms undisturbed = MissionStealthDifficulty.CalculateWatchTerms(defender);
        defender.CommittedAttention = undisturbed.Patrol / 2f;
        WatchTerms drawn = MissionStealthDifficulty.CalculateWatchTerms(defender);

        Assert.True(undisturbed.Patrol > 0f, "fixture must have patrol effort to draw away");
        Assert.Equal(undisturbed.Patrol / 2f, drawn.Patrol, precision: 4);
        Assert.Equal(undisturbed.Ambient, drawn.Ambient, precision: 4);
        Assert.True(drawn.Total < undisturbed.Total);
    }

    // Only once the screen is fully committed does the draw begin to reach the troops merely present.
    // This is what makes prising defenders loose require real commitment rather than one lucky roll.
    [Fact]
    public void CommittedAttention_SpillsIntoAmbientOnceThePatrolTermIsExhausted()
    {
        Region region = CreateRegion();
        RegionFaction defender = AddHordeFaction(region, CreateFaction(20, "Swarm"), 10_000, intel: 0f);
        SendOnMission(defender, MissionType.Patrol, 1_000);

        WatchTerms undisturbed = MissionStealthDifficulty.CalculateWatchTerms(defender);
        float spill = 0.25f;
        defender.CommittedAttention = undisturbed.Patrol + spill;
        WatchTerms drawn = MissionStealthDifficulty.CalculateWatchTerms(defender);

        Assert.Equal(0f, drawn.Patrol, precision: 4);
        Assert.Equal(undisturbed.Ambient - spill, drawn.Ambient, precision: 4);
    }

    // Sensors, informants, and standing awareness of your own ground do not turn their heads to watch
    // a demonstration, however convincing it is.
    [Fact]
    public void CommittedAttention_NeverReducesSurveillance()
    {
        Region region = CreateRegion();
        RegionFaction defender = AddHordeFaction(region, CreateFaction(20, "Swarm"), 10_000, intel: 4f);
        SendOnMission(defender, MissionType.Patrol, 1_000);

        WatchTerms undisturbed = MissionStealthDifficulty.CalculateWatchTerms(defender);
        defender.CommittedAttention = 100f;
        WatchTerms drawn = MissionStealthDifficulty.CalculateWatchTerms(defender);

        Assert.True(undisturbed.Surveillance > 0f);
        Assert.Equal(undisturbed.Surveillance, drawn.Surveillance, precision: 4);
    }

    // An unbounded draw must not push terms negative and start handing out free successes - the same
    // failure class the log10(1 + x) shape exists to close off.
    [Fact]
    public void CommittedAttention_CannotDriveTermsBelowZero()
    {
        Region region = CreateRegion();
        RegionFaction defender = AddHordeFaction(region, CreateFaction(20, "Swarm"), 10_000, intel: 3f);
        SendOnMission(defender, MissionType.Patrol, 1_000);
        defender.CommittedAttention = 1_000f;

        WatchTerms drawn = MissionStealthDifficulty.CalculateWatchTerms(defender);

        Assert.Equal(0f, drawn.Patrol, precision: 4);
        Assert.Equal(0f, drawn.Ambient, precision: 4);
        Assert.Equal(drawn.Surveillance, drawn.Total, precision: 4);
    }

    // A feint against ground nobody is watching accomplishes nothing, so there is nothing to draw.
    [Fact]
    public void CommittedAttention_OnUnwatchedGround_ChangesNothing()
    {
        Region region = CreateRegion();
        RegionFaction defender = AddHordeFaction(region, CreateFaction(20, "Swarm"), 0, intel: 0f);
        defender.CommittedAttention = 5f;

        WatchTerms drawn = MissionStealthDifficulty.CalculateWatchTerms(defender);

        Assert.Equal(0f, drawn.Total, precision: 4);
    }

    private static Squad SendOnMission(RegionFaction rf, MissionType missionType, int battleValue)
    {
        Assert.True(
            battleValue % TestSoldierBattleValue == 0,
            $"Patrol fixture battle value {battleValue} must be a whole number of test soldiers.");
        int soldiers = battleValue / TestSoldierBattleValue;
        Squad squad = CreateSquadOfSize($"{rf.PlanetFaction.Faction.Name} {missionType}", soldiers);
        // The Order constructor is what wires Squad.CurrentOrders back to the order, which is the
        // link GetPatrolStrength reads; constructing it is not a no-op even though the result is
        // unused here.
        _ = new Order(
            [squad],
            isQuiet: false,
            isActivelyEngaging: false,
            levelOfAggression: Aggression.Normal,
            mission: new Mission(missionType, rf, missionSize: 0));
        squad.CurrentRegion = rf.Region;
        rf.LandedSquads.Add(squad);
        return squad;
    }

    private static Squad CreateSquadOfSize(string name, int soldiers)
    {
        Squad squad = TestModelFactory.CreateSquad(name);
        for (int i = 0; i < soldiers; i++)
        {
            squad.AddSquadMember(TestModelFactory.CreateSoldier(name: $"{name} {i}"));
        }
        return squad;
    }

    // A civilian-base defender: its army is its Garrison, the way an Imperial PDF works. Population is
    // set far above the garrison so the Garrison clamp does not eat the value.
    private static RegionFaction AddGarrisonFaction(
        Region region, Faction faction, long garrison, float intel)
    {
        faction.PopulationIsMilitary = false;
        return AddEnemy(
            region, faction, population: garrison * 100, organization: 100, intel: intel,
            garrison: garrison);
    }

    // A PopulationIsMilitary horde: its whole population is its army and its Garrison stays zero.
    private static RegionFaction AddHordeFaction(
        Region region, Faction faction, long population, float intel) =>
        AddEnemy(region, faction, population, organization: 100, intel: intel);

    private static RegionFaction AddEnemy(
        Region region, Faction faction, long population, int organization, float intel,
        long garrison = 0)
    {
        PlanetFaction planetFaction = new(faction) { IsPublic = true };
        RegionFaction regionFaction = new(planetFaction, region)
        {
            Population = population,
            Garrison = garrison,
            Organization = organization,
            IsPublic = true
        };
        planetFaction.SetRegionIntel(region, intel);
        region.RegionFactionMap[faction.Id] = regionFaction;
        return regionFaction;
    }

    // The aggregation reads only the region's faction map and each faction's own intel, so the pure
    // model tests get by with no owning planet.
    private static Region CreateRegion() =>
        new(0, null, 0, "Test Region", new RegionCoordinate(0, 0), 0);

    // A non-player, non-default faction defaults to PopulationIsMilitary, so MilitaryStrength is its
    // Population and its Garrison stays zero — the horde case most of these tests are about.
    private static Faction CreateFaction(int id, string name)
    {
        return new Faction(
            id,
            name,
            Color.Red,
            isPlayerFaction: false,
            isDefaultFaction: false,
            canInfiltrate: false,
            GrowthType.Conversion,
            new Dictionary<int, Species>(),
            new Dictionary<int, SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
    }
}
