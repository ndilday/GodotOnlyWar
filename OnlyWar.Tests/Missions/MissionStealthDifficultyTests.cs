using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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

// The stealth/tactics difficulty model shared by every mission step that has to move through a
// region unseen, and the two ways it used to blow up: Log(0) = -infinity for a troop count of zero,
// and reading raw Garrison (always 0 for a PopulationIsMilitary horde) instead of deployed strength.
// Together those made a Tyranid- or cult-held region trivially infiltrable and trivially sabotaged.
public class MissionStealthDifficultyTests
{
    // --- the shared troop-magnitude guard ---

    [Theory]
    [InlineData(0L, 0.0)]
    [InlineData(1L, 0.0)]
    [InlineData(10L, 1.0)]
    [InlineData(1000L, 3.0)]
    [InlineData(1_000_000L, 6.0)]
    public void TroopMagnitude_IsLog10_FlooredAtZeroInsteadOfNegativeInfinity(
        long troops, double expected)
    {
        float magnitude = MissionStealthDifficulty.TroopMagnitude(troops);

        Assert.True(float.IsFinite(magnitude));
        Assert.Equal(expected, magnitude, 4);
    }

    // --- the aggregated region model ---

    [Fact]
    public void Calculate_NoDeployableTroopsAnywhere_LeavesAFiniteDifficulty()
    {
        // Present factions with population but no organization, so deployed strength is 0 across the
        // whole region. Unguarded this is Log(0) = -infinity, and the margin that comes back out of
        // the check is +infinity: the intruder slips in for free no matter how badly it rolls.
        Region region = CreateRegion();
        AddEnemy(region, CreateFaction(20, "Alpha"), population: 1_000, organization: 0, intel: 0f);
        AddEnemy(region, CreateFaction(21, "Beta"), population: 1_000, organization: 0, intel: 0f);

        StealthDifficultyTerms terms = MissionStealthDifficulty.Calculate(
            region, intruderHeadcount: 5, intruder: null);

        Assert.Equal(0f, terms.TroopMod);
        Assert.True(float.IsFinite(terms.Total));
    }

    [Fact]
    public void Calculate_EmptyIntruderForce_LeavesAFiniteDifficulty()
    {
        // The intruder's own headcount goes through the same log, and a force can be emptied of able
        // soldiers mid-mission (the check then auto-fails downstream). Log(0) there would flip the
        // difficulty to -infinity and hand that dead force a guaranteed success on the way out.
        Region region = CreateRegion();
        AddEnemy(region, CreateFaction(20, "Swarm"), population: 10_000, organization: 100, intel: 0f);

        StealthDifficultyTerms terms = MissionStealthDifficulty.Calculate(
            region, intruderHeadcount: 0, intruder: null);

        Assert.Equal(0f, terms.OwnTroopMod);
        Assert.True(float.IsFinite(terms.Total));
    }

    [Fact]
    public void Calculate_ZeroGarrisonHorde_IsMeasuredByItsPopulation()
    {
        // A PopulationIsMilitary faction's army IS its population; it carries no Garrison at all. The
        // troop term must come from deployed strength, or infiltrating a Tyranid-held region faces
        // no troop difficulty whatsoever.
        Region region = CreateRegion();
        RegionFaction horde = AddEnemy(
            region, CreateFaction(20, "Swarm"), population: 1_000_000, organization: 100, intel: 0f);

        StealthDifficultyTerms terms = MissionStealthDifficulty.Calculate(
            region, intruderHeadcount: 1, intruder: null);

        Assert.Equal(0, horde.Garrison);
        Assert.Equal(1_000_000, horde.GetDeployedStrength());
        Assert.Equal(6.0, terms.TroopMod, 4);
    }

    [Fact]
    public void Calculate_TroopTermSumsEveryEnemyFactionInTheRegion()
    {
        // Detection is a property of the region, so two 500-strong factions are one 1,000-strong
        // problem (log10 = 3), not whichever single faction the mission happens to be aimed at
        // (log10(500) = 2.7). This is the same enemy set SelectSpotter draws the interceptor from.
        Region region = CreateRegion();
        AddEnemy(region, CreateFaction(20, "Swarm"), population: 500, organization: 100, intel: 0f);
        AddEnemy(region, CreateFaction(21, "Cult"), population: 500, organization: 100, intel: 0f);

        StealthDifficultyTerms terms = MissionStealthDifficulty.Calculate(
            region, intruderHeadcount: 1, intruder: null);

        Assert.Equal(2, terms.EnemyCount);
        Assert.Equal(3.0, terms.TroopMod, 4);
    }

    [Fact]
    public void Calculate_DetectionSumsEveryEnemyFactionsAwareness()
    {
        // Likewise for awareness: a hidden cult watching the ground makes the region harder to cross
        // even when the mission is aimed at the horde sitting on top of it.
        Region region = CreateRegion();
        AddEnemy(region, CreateFaction(20, "Swarm"), population: 100, organization: 100, intel: 2f);
        AddEnemy(region, CreateFaction(21, "Cult"), population: 100, organization: 100, intel: 4f);

        StealthDifficultyTerms terms = MissionStealthDifficulty.Calculate(
            region, intruderHeadcount: 1, intruder: null);

        Assert.Equal(3.0, terms.Detection, 4);
    }

    [Fact]
    public void Calculate_IgnoresFactionsWithNeitherTroopsNorAwareness()
    {
        // A faction that has been wiped out of the region is not watching it. It must not be counted
        // as a detecting enemy, and (with nothing left to sum) the guard must still hold.
        Region region = CreateRegion();
        AddEnemy(region, CreateFaction(20, "Ghosts"), population: 0, organization: 100, intel: 0f);

        StealthDifficultyTerms terms = MissionStealthDifficulty.Calculate(
            region, intruderHeadcount: 4, intruder: null);

        Assert.Equal(0, terms.EnemyCount);
        Assert.Equal(0f, terms.TroopMod);
        Assert.True(float.IsFinite(terms.Total));
    }

    // --- the sabotage steps, end to end ---

    // SabotageStealthMissionStep read Log(Garrison), so against a horde it computed a difficulty of
    // -infinity and every stealth roll came back +infinity: the force walked in unseen every single
    // day regardless of how bad its scouts were. An untrained squad now gets caught.
    [Fact]
    public void SabotageStealth_UntrainedForceAgainstAZeroGarrisonHorde_IsDetected()
    {
        MissionContext context = CreateSabotageContext(hordePopulation: 100_000_000, trained: false);

        new SabotageStealthMissionStep().ExecuteMissionStep(CreateExecution(context), 0f, null);

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

        new SabotageStealthMissionStep().ExecuteMissionStep(CreateExecution(context), 0f, null);

        Assert.Equal(MissionContext.MissionDurationDays, context.Log.Count(l => l.Contains("plants explosives")));
        Assert.True(float.IsFinite(context.Impact));
        Assert.True(context.Impact > 0f);
    }

    // ...and the horde's numbers have to actually matter. FixedRNG rolls z = 0, so each day's margin
    // is exactly (skill - difficulty) / 5 and the whole-mission gap between a 100-strong and a
    // 1,000,000-strong defender is the four orders of magnitude between them, spread over the
    // mission's days. Under the old raw-Garrison read both were 0 and the two runs were identical.
    [Fact]
    public void PerformSabotage_LargerHorde_IsHarderInProportionToItsMagnitude()
    {
        MissionContext small = CreateSabotageContext(hordePopulation: 100, trained: true);
        MissionContext large = CreateSabotageContext(hordePopulation: 1_000_000, trained: true);

        new SabotageStealthMissionStep().ExecuteMissionStep(CreateExecution(small), 0f, null);
        new SabotageStealthMissionStep().ExecuteMissionStep(CreateExecution(large), 0f, null);

        // Four orders of magnitude, divided by the check's 5-point-per-sigma scale, once per day.
        const float expectedGap = MissionContext.MissionDurationDays * (6f - 2f) / 5f;
        Assert.True(small.Impact > large.Impact);
        Assert.Equal(expectedGap, small.Impact - large.Impact, 3);
    }

    // A sabotage force in the region it already holds, so there is no infiltration or exfiltration
    // to muddy the day count. The defender is a PopulationIsMilitary horde: its whole army sits in
    // Population and its Garrison is zero, which is the case the old code could not price.
    private static MissionContext CreateSabotageContext(long hordePopulation, bool trained)
    {
        Planet planet = new(1, "Test Planet", new Coordinate(0, 0), 1, null, 0, 0);
        Region region = new(1, planet, 0, "Target Region", new RegionCoordinate(0, 0), 0);
        planet.Regions[0] = region;
        Faction horde = CreateFaction(20, "Swarm");
        RegionFaction target = new(new PlanetFaction(horde), region)
        {
            Population = hordePopulation
        };
        region.RegionFactionMap[horde.Id] = target;

        Squad squad = TestModelFactory.CreateSquad(
            "Saboteur Squad",
            CreateSaboteur(TestModelFactory.SergeantTemplate, "Saboteur Sergeant", trained),
            CreateSaboteur(TestModelFactory.MarineTemplate, "Saboteur", trained));
        squad.CurrentRegion = region;
        Order order = new(
            [squad],
            Disposition.Raiding,
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

    private static RegionFaction AddEnemy(
        Region region, Faction faction, long population, int organization, float intel)
    {
        PlanetFaction planetFaction = new(faction) { IsPublic = true };
        RegionFaction regionFaction = new(planetFaction, region)
        {
            Population = population,
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
    // Population and its Garrison stays zero — the horde case these tests are about.
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
