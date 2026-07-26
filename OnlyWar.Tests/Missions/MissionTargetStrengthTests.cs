using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Missions.Ambush;
using OnlyWar.Helpers.Missions.Assassinate;
using OnlyWar.Helpers.Missions.Diversion;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.Turns;
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

// The rest of the Log(Garrison) family, alongside MissionStealthDifficultyTests. Every mission step
// and mission generator that sizes itself against a defender used to read the raw Garrison, which is
// permanently 0 for a PopulationIsMilitary faction (Tyranids, cults) whose army IS its Population.
// Log(0) is -infinity, so a hive-fleet region was the easiest ground in the game to sneak through,
// assassinate in, and feint against - and the generators built missions with garbage sizes.
public class MissionTargetStrengthTests
{
    // --- ambush positioning ---

    // PositionAmbushMissionStep's stealth check had the unguarded log, so against a horde its
    // difficulty was -infinity and the margin +infinity: the ambushers always got into position no
    // matter who they were. An untrained squad now gets spotted first and has to take the meeting
    // engagement instead. (Neither branch has an OpFor to fight here - the stub faction has no squad
    // templates - so the two "no combat-capable forces" logs are what distinguish them.)
    //
    // The defender has to be actively searching, not merely enormous: under the search-effort model a
    // dormant hive fleet is genuinely easy to set an ambush against, and the raw population is capped
    // out of mattering. Watching plus patrolling is what spoils the setup now.
    [Fact]
    public void PositionAmbush_UntrainedForceAgainstASearchedRegion_IsDetected()
    {
        MissionContext context = CreateContext(
            MissionType.Ambush, hordePopulation: 100_000_000, trained: false,
            defenderIntel: 6f, defenderPatrollers: 250);

        new PositionAmbushMissionStep().ExecuteMissionStep(CreateExecution(context), 0f, null);

        Assert.Contains(context.Log, line => line.Contains("for engagement"));
        Assert.DoesNotContain(context.Log, line => line.Contains("for ambush"));
    }

    [Fact]
    public void PositionAmbush_TrainedForceAgainstAThinlyHeldRegion_GetsIntoPosition()
    {
        MissionContext context = CreateContext(
            MissionType.Ambush, hordePopulation: 100, trained: true);

        new PositionAmbushMissionStep().ExecuteMissionStep(CreateExecution(context), 0f, null);

        Assert.Contains(context.Log, line => line.Contains("for ambush"));
    }

    // --- assassination approach ---

    // Same unguarded log in AssassinateStealthMissionStep. The defender's search effort now prices
    // the approach, so an untrained force is detected each day instead of walking in, and the loop
    // stops at the day budget rather than recursing forever through DetectedMissionStep.
    [Fact]
    public void AssassinateStealth_UntrainedForceAgainstASearchedRegion_IsDetected()
    {
        MissionContext context = CreateContext(
            MissionType.Assassination, hordePopulation: 100_000_000, trained: false,
            defenderIntel: 6f, defenderPatrollers: 250);

        new AssassinateStealthMissionStep().ExecuteMissionStep(CreateExecution(context), 0f, null);

        Assert.Contains(context.Log, line => line.Contains("detected and intercepted"));
        Assert.DoesNotContain(context.Log, line => line.Contains("located the assassination target"));
        Assert.Equal(MissionContext.MissionDurationDays, context.DaysElapsed);
    }

    [Fact]
    public void AssassinateStealth_TrainedForceAgainstAThinlyHeldRegion_ReachesTheTarget()
    {
        MissionContext context = CreateContext(
            MissionType.Assassination, hordePopulation: 100, trained: true);

        new AssassinateStealthMissionStep().ExecuteMissionStep(CreateExecution(context), 0f, null);

        Assert.Contains(context.Log, line => line.Contains("located the assassination target"));
    }

    // PerformAssassinationMissionStep's own Tactics check is target-anchored (like sabotage's), so it
    // reads the target faction's deployed strength directly rather than the region aggregate, and it
    // deliberately stays OFF the search-effort model: a bodyguard is part of the screen around the
    // target whether it is out patrolling or standing at a door, so neither the ambient cap nor the
    // patrol/static split belongs here. It takes only Magnitude's log10(1 + x) shape, so the term is
    // finite, and driven by the Population a horde actually fights with even though its Garrison is
    // zero.
    [Fact]
    public void PerformAssassination_TargetTroopTerm_ComesFromDeployedStrengthNotGarrison()
    {
        RegionFaction horde = CreateContext(
            MissionType.Assassination, hordePopulation: 1_000_000, trained: true)
            .Order.Mission.RegionFaction;

        Assert.Equal(0, horde.Garrison);
        Assert.Equal(1_000_000, horde.GetDeployedStrength());
        Assert.Equal(6.0, MissionStealthDifficulty.Magnitude(horde.GetDeployedStrength()), 4);
    }

    // --- diversion ---

    // DemonstrateForceMissionStep was already guarded against -infinity by Max(Garrison, 1), so it
    // never produced a garbage number - it produced a wrong one. Every horde read as a garrison of 1,
    // meaning a feint against a hive fleet was exactly as easy as a feint against an empty region.
    // FixedRNG rolls z = 0, so each day's margin is (skill - difficulty) / 5 and the whole-mission
    // gap between the two defenders is the four orders of magnitude between them, once per day.
    [Fact]
    public void DemonstrateForce_LargerHorde_IsHarderInProportionToItsMagnitude()
    {
        MissionContext small = CreateContext(MissionType.Diversion, 100, trained: true);
        MissionContext large = CreateContext(MissionType.Diversion, 1_000_000, trained: true);

        new DemonstrateForceMissionStep().ExecuteMissionStep(CreateExecution(small), 0f, null);
        new DemonstrateForceMissionStep().ExecuteMissionStep(CreateExecution(large), 0f, null);

        float expectedGap = MissionContext.MissionDurationDays
            * (MissionStealthDifficulty.Magnitude(1_000_000)
               - MissionStealthDifficulty.Magnitude(100)) / 5f;
        Assert.True(float.IsFinite(large.Impact));
        Assert.True(small.Impact > large.Impact);
        Assert.Equal(expectedGap, small.Impact - large.Impact, 3);
    }

    // --- the special-mission generators ---

    // GenerateAmbushMission sized the mission from Log10(Garrison). Against a horde that is
    // Log10(0) = -infinity, and the cast to int saturates to int.MinValue, so the ambush was created
    // with a wildly negative size. PositionAmbushMissionStep then raises 10 to it, giving the
    // opposing force a target battle value of 0: an ambush mission with no ambushers in it.
    [Fact]
    public void GenerateAmbushMission_AgainstAZeroGarrisonHorde_HasAUsableSize()
    {
        (PlanetTurnProcessor processor, RegionFaction horde, List<Mission> generated) =
            CreateGenerator(hordePopulation: 100_000_000, branchZ: AmbushBranchZ, sizeZ: 0.0);

        processor.HandlePublicFactionIntelligence(horde, specMissionBudget: 1.0f);

        Mission ambush = Assert.Single(generated);
        Assert.Equal(MissionType.Ambush, ambush.MissionType);
        Assert.Equal(0, horde.Garrison);
        Assert.Equal(1, ambush.MissionSize);
    }

    // The magnitude still has to cap the size: a rolled size of 4 stands against a hive fleet but is
    // cut to 2 by a 100-strong holding. Under the raw-Garrison read the cap was int.MinValue for both.
    [Theory]
    [InlineData(100L, 2)]
    [InlineData(100_000_000L, 4)]
    public void GenerateAmbushMission_SizeIsCappedByTheDefendersMagnitude(
        long hordePopulation, int expectedSize)
    {
        (PlanetTurnProcessor processor, RegionFaction horde, List<Mission> generated) =
            CreateGenerator(hordePopulation, branchZ: AmbushBranchZ, sizeZ: 3.0);

        processor.HandlePublicFactionIntelligence(horde, specMissionBudget: 1.0f);

        Mission ambush = Assert.Single(generated);
        Assert.Equal(MissionType.Ambush, ambush.MissionType);
        Assert.Equal(expectedSize, ambush.MissionSize);
    }

    // GenerateAssassinationMission has the same shape over Population. A horde's Population is
    // non-zero, but a region faction can be ground down to nothing and still be walked through here,
    // and Log10(0) casts to int.MinValue just the same - as does Log10(1) rounding the cap to 0.
    // Mission size selects the target's rank from the faction's HQ ladder, so it must never come out
    // below 1: at 0 or less, Math.Clamp(Tier - 1, ...) in ForceGenerator silently picks the weakest
    // HQ available regardless of how large the faction actually is.
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(100_000_000L)]
    public void GenerateAssassinationMission_AlwaysPicksARealTargetRank(long hordePopulation)
    {
        (PlanetTurnProcessor processor, RegionFaction horde, List<Mission> generated) =
            CreateGenerator(hordePopulation, branchZ: AssassinationBranchZ, sizeZ: 0.0);

        processor.HandlePublicFactionIntelligence(horde, specMissionBudget: 1.0f);

        Mission assassination = Assert.Single(generated);
        Assert.Equal(MissionType.Assassination, assassination.MissionType);
        Assert.True(assassination.MissionSize >= 1);
    }

    // ...and it is still capped by the faction's presence, so the ladder is climbed by hive size.
    [Theory]
    [InlineData(100L, 2)]
    [InlineData(100_000_000L, 4)]
    public void GenerateAssassinationMission_SizeIsCappedByTheFactionsPopulation(
        long hordePopulation, int expectedSize)
    {
        (PlanetTurnProcessor processor, RegionFaction horde, List<Mission> generated) =
            CreateGenerator(hordePopulation, branchZ: AssassinationBranchZ, sizeZ: 3.0);

        processor.HandlePublicFactionIntelligence(horde, specMissionBudget: 1.0f);

        Assert.Equal(expectedSize, Assert.Single(generated).MissionSize);
    }

    // --- fixtures ---

    // HandlePublicFactionIntelligence rolls one z to pick which special mission to generate, then the
    // generator rolls a second z for the mission's size. These select the branch.
    private const double AmbushBranchZ = 0.5;
    private const double AssassinationBranchZ = 2.5;

    // A turn processor over a single region held by one public PopulationIsMilitary horde, with both
    // z-rolls pinned: the first picks the branch, the second the rolled (pre-cap) mission size.
    private static (PlanetTurnProcessor, RegionFaction, List<Mission>) CreateGenerator(
        long hordePopulation, double branchZ, double sizeZ)
    {
        Planet planet = new(1, "Test Planet", new Coordinate(0, 0), 1, null, 0, 0);
        Region region = new(1, planet, 0, "Target Region", new RegionCoordinate(0, 0), 0);
        planet.Regions[0] = region;
        Faction faction = CreateFaction(20, "Swarm");
        PlanetFaction planetFaction = new(faction) { IsPublic = true };
        planet.PlanetFactionMap[faction.Id] = planetFaction;
        RegionFaction horde = new(planetFaction, region)
        {
            Population = hordePopulation,
            IsPublic = true
        };
        region.RegionFactionMap[faction.Id] = horde;

        GameSession session = new(
            new GameRulesData(), new Sector(), new Date(1, 1, 1), new SequencedZRng(branchZ, sizeZ));
        List<Mission> generated = [];
        return (new PlanetTurnProcessor(session, generated), horde, generated);
    }

    // A force standing in the region it is operating against, so there is no infiltration or
    // exfiltration to muddy the day count, facing a PopulationIsMilitary horde whose whole army sits
    // in Population and whose Garrison is therefore zero - the case none of these steps could price.
    // The horde is dormant unless a test asks for intel and patrollers: MissionStealthDifficulty
    // scores what a defender is DOING, so "huge" and "hunting" are now separate dials and a test that
    // wants a hard-to-cross region has to turn the second one.
    private static MissionContext CreateContext(
        MissionType missionType,
        long hordePopulation,
        bool trained,
        float defenderIntel = 0f,
        int defenderPatrollers = 0)
    {
        Planet planet = new(1, "Test Planet", new Coordinate(0, 0), 1, null, 0, 0);
        Region region = new(1, planet, 0, "Target Region", new RegionCoordinate(0, 0), 0);
        planet.Regions[0] = region;
        Faction horde = CreateFaction(20, "Swarm");
        PlanetFaction hordePlanetFaction = new(horde);
        RegionFaction target = new(hordePlanetFaction, region)
        {
            Population = hordePopulation
        };
        hordePlanetFaction.SetRegionIntel(region, defenderIntel);
        region.RegionFactionMap[horde.Id] = target;
        if (defenderPatrollers > 0)
        {
            // GetPatrolStrength counts the members of squads landed here under a Patrol or Recon
            // order; the Order constructor is what wires Squad.CurrentOrders back to the order.
            Squad patrol = TestModelFactory.CreateSquad("Defender Patrol");
            for (int i = 0; i < defenderPatrollers; i++)
            {
                patrol.AddSquadMember(TestModelFactory.CreateSoldier(name: $"Patroller {i}"));
            }
            patrol.CurrentRegion = region;
            _ = new Order(
                [patrol],
                Disposition.Mobile,
                isQuiet: false,
                isActivelyEngaging: false,
                levelOfAggression: Aggression.Normal,
                mission: new Mission(MissionType.Patrol, target, missionSize: 0));
            target.LandedSquads.Add(patrol);
        }

        Squad squad = TestModelFactory.CreateSquad(
            "Raider Squad",
            CreateRaider(TestModelFactory.SergeantTemplate, "Raider Sergeant", trained),
            CreateRaider(TestModelFactory.MarineTemplate, "Raider", trained));
        squad.CurrentRegion = region;
        Order order = new(
            [squad],
            Disposition.Raiding,
            isQuiet: true,
            isActivelyEngaging: false,
            levelOfAggression: Aggression.Normal,
            mission: new Mission(missionType, target, missionSize: 1));

        return new MissionContext(order, [new BattleSquad(true, squad)], []);
    }

    // Trained raiders clear the checks in the loop; untrained ones fall back to the unskilled
    // attribute value and are caught, which is what makes the difficulty visible at all.
    private static Soldier CreateRaider(SoldierTemplate template, string name, bool trained) =>
        trained
            ? TestModelFactory.CreateSoldier(
                template,
                name,
                skills: [new Skill(TestSkills.Stealth, 256), new Skill(TestSkills.Tactics, 256)])
            : TestModelFactory.CreateSoldier(template, name);

    private static MissionExecutionContext CreateExecution(MissionContext context) =>
        TestExecutionContextFactory.CreateMission(context, new FixedRNG());

    // A non-player, non-default faction defaults to PopulationIsMilitary, so MilitaryStrength is its
    // Population and its Garrison stays zero.
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

    // FixedRNG pins every z-roll at 0, which can only ever select the ambush branch with a rolled
    // size of 1. This hands out a prepared sequence instead, holding the last value once exhausted,
    // so a test can choose the branch and the rolled size independently.
    private sealed class SequencedZRng(params double[] zValues) : IRNG
    {
        private int _index;

        public double GetDoubleInRange(double lowerBound, double upperBound) => lowerBound;
        public double GetLinearDouble() => 0.0;
        public int GetIntBelowMax(int min, int max) => min;

        public double NextRandomZValue() =>
            zValues[_index < zValues.Length - 1 ? _index++ : zValues.Length - 1];
    }
}
