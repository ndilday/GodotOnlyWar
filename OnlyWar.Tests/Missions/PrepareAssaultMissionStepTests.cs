using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Missions.Assault;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Missions;

public class PrepareAssaultMissionStepTests
{
    [Fact]
    public void UnopposedAssault_DestroysOneAttackerMultipleOfDisorganizedBvPerDay()
    {
        Faction attacker = CreateFaction(1, "Attackers", isPlayer: true);
        Faction defender = CreateFaction(2, "Defenders", isDefault: true);
        Planet planet = new(1, "Terra", new Coordinate(0, 0), 1, null, 0, 0);
        Region region = new(1, planet, 0, "Terra Lambda", new RegionCoordinate(0, 0), 0);
        planet.Regions[0] = region;
        RegionFaction target = AddPresence(region, defender);
        target.Population = 10_000;
        target.Garrison = 1_000;
        target.Organization = 0;

        Squad squad = TestModelFactory.CreateSquad(
            "Assault force", TestModelFactory.CreateSoldier());
        squad.CurrentRegion = region;
        Order order = new([squad], false, true, Aggression.Aggressive,
            new Mission(MissionType.Advance, target, 0));
        MissionContext context = new(order, [new BattleSquad(true, squad)], []);

        new MissionStepDriver(
            TestExecutionContextFactory.CreateMission(context, new FixedRNG()),
            new PrepareAssaultMissionStep()).RunToCompletion();

        long expected = context.StartingMissionBattleValue
            * MissionContext.MissionDurationDays;
        Assert.Equal(MissionContext.MissionDurationDays, context.DaysElapsed);
        Assert.Equal(expected, context.DisorganizedDefenderBattleValueDestroyed);
        Assert.Equal(
            MissionContext.MissionDurationDays,
            context.Log.Count(line => line.Contains("unopposed")));

        new MissionAftermathProcessor(null, null).ApplyMissionResults([context]);
        Assert.Equal(1_000 - expected, target.MilitaryStrength);
        Assert.Equal(0, target.OrganizedMilitaryStrength);
    }

    [Fact]
    public void RegionalDefenders_IncludeAlliedFactionButExcludeEnemy()
    {
        Faction player = CreateFaction(1, "Chapter", isPlayer: true);
        Faction imperial = CreateFaction(2, "Imperial", isDefault: true);
        Faction enemy = CreateFaction(3, "Cult");
        Planet planet = new(1, "Terra", new Coordinate(0, 0), 1, null, 0, 0);
        Region region = new(1, planet, 0, "Terra Lambda", new RegionCoordinate(0, 0), 0);
        planet.Regions[0] = region;
        FactionRelationshipLedger relationships = new();
        planet.AttachRelationshipLedger(relationships);
        relationships.SetStance(player, imperial, FactionStance.Allied);
        RegionFaction target = AddPresence(region, imperial);
        RegionFaction playerPresence = AddPresence(region, player);
        RegionFaction enemyPresence = AddPresence(region, enemy);
        Squad alliedDefender = AddDefender(playerPresence, "Allied defenders");
        Squad enemyDefender = AddDefender(enemyPresence, "Enemy defenders");

        List<Squad> defenders = PrepareAssaultMissionStep.GetRegionalDefensiveSquads(target);

        Assert.Contains(alliedDefender, defenders);
        Assert.DoesNotContain(enemyDefender, defenders);
    }

    // The defence mobilises the region's RESERVE - the battle value its controller held back to hold the
    // ground (FactionStrategyController.CalculateRequiredDefensiveBattleValue) - and reads it in battle
    // value directly. The budget is what changed here: it used to be raw RegionFaction.Garrison, which is
    // zero for every revealed non-Imperial faction. The original guard this test carries is still the
    // point of it, though: the generated force must not exceed its budget. An x10 conversion once lived
    // on this path and massively over-mobilised defenders.
    [Fact]
    public void AssembleDefendingForce_MobilisesTheReserveInBattleValueDirectly()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        Faction pdf = rules.Factions.Single(f => f.IsDefaultFaction);

        Planet planet = new(1, "Terra", new Coordinate(0, 0), 1, null, 0, 0);
        Region region = new(1, planet, 0, "Terra Lambda", new RegionCoordinate(0, 0), 0);
        planet.Regions[0] = region;
        RegionFaction target = AddPresence(region, pdf);
        target.Population = 10_000;
        // Sized so the reserve is comfortably squad-capable. With nothing visible next door the reserve is
        // the 20% floor, so a garrison of only a handful of troopers would reserve less than one squad's
        // worth and mobilise nothing at all - which is correct behaviour, but tests nothing here.
        target.Garrison = StrategicCombatRules.PdfTrooperBattleValue * 200;

        long reserve = FactionStrategyController.CalculateRequiredDefensiveBattleValue(target);

        List<BattleSquad> defenders = new PrepareAssaultMissionStep()
            .AssembleDefendingForce(
                target,
                attackerMarginOfSuccess: 0f,
                new SeededRNG(1));

        long generatedBattleValue = defenders
            .SelectMany(squad => squad.Squad.Members)
            .Sum(soldier => (long)soldier.Template.BattleValue);

        Assert.True(reserve > 0, "fixture must reserve something to mobilise");
        Assert.True(reserve < target.Garrison, "the reserve is a share of strength, not all of it");
        Assert.NotEmpty(defenders);
        Assert.InRange(generatedBattleValue, 1, reserve);
        Assert.True(generatedBattleValue < StrategicCombatRules.MassCombatBattleValueFloor);
    }

    // --- patrol detection: whether a screen was looking the right way when the blow landed ---
    //
    // These use repeated seeds rather than a single roll, because the properties that matter are
    // monotonic rather than absolute: the exact detection rate depends on the fixture's Tactics values,
    // but the DIRECTION of each term is the design commitment.

    // Callers that supply no rules keep the old unconditional behaviour rather than silently dropping the
    // patrol out of the defence.
    [Fact]
    public void PatrolDetection_WithoutRules_DefaultsToJoiningTheDefence()
    {
        (BattleSquad patrol, RegionFaction _) = CreatePatrol();

        Assert.True(PrepareAssaultMissionStep.PatrolDetectedAttack(
            patrol, attackerBattleValue: 1_000, defenderTactics: null, random: null));
    }

    // A full advance crossing into the region is near-impossible to overlook; a small raid slipping past
    // a screen is a genuine possibility. This is what keeps "the patrol failed to notice ten thousand
    // orks" from being a reachable outcome.
    [Fact]
    public void PatrolDetection_IsEasierAgainstALargerAttackingForce()
    {
        int smallForce = CountDetections(attackerBattleValue: 1, committedAttention: 0f);
        int largeForce = CountDetections(attackerBattleValue: 10_000_000, committedAttention: 0f);

        Assert.True(
            largeForce > smallForce,
            $"a larger attacker must be easier to spot (small={smallForce}, large={largeForce})");
    }

    // Decision 9: a diverted patrol has not failed to detect, it detected the wrong thing. This is the
    // payoff for a successful feint, and it is what makes the diversion/assault combination work.
    [Fact]
    public void PatrolDetection_IsHarderWhenAttentionHasBeenDrawnAway()
    {
        int undiverted = CountDetections(attackerBattleValue: 10_000_000, committedAttention: 0f);
        int diverted = CountDetections(attackerBattleValue: 10_000_000, committedAttention: 3f);

        Assert.True(
            diverted < undiverted,
            $"drawing the screen aside must cost it detections (undiverted={undiverted}, diverted={diverted})");
    }

    private const int DetectionSampleCount = 200;

    private static int CountDetections(long attackerBattleValue, float committedAttention)
    {
        int detections = 0;
        for (int seed = 0; seed < DetectionSampleCount; seed++)
        {
            (BattleSquad patrol, RegionFaction presence) = CreatePatrol();
            presence.CommittedAttention = committedAttention;
            if (PrepareAssaultMissionStep.PatrolDetectedAttack(
                patrol, attackerBattleValue, TestSkills.Tactics, new SeededRNG(seed)))
            {
                detections++;
            }
        }
        return detections;
    }

    private static (BattleSquad, RegionFaction) CreatePatrol()
    {
        Planet planet = new(1, "Terra", new Coordinate(0, 0), 1, null, 0, 0);
        Region region = new(1, planet, 0, "Terra Lambda", new RegionCoordinate(0, 0), 0);
        planet.Regions[0] = region;
        RegionFaction presence = AddPresence(region, CreateFaction(9, "Screen Force"));

        Squad squad = TestModelFactory.CreateSquad("Screen", TestModelFactory.CreateSoldier());
        squad.CurrentRegion = region;
        squad.CurrentOrders = new Order([squad], true, false,
            Aggression.Normal, new Mission(MissionType.Patrol, presence, 0));
        presence.LandedSquads.Add(squad);
        return (new BattleSquad(false, squad), presence);
    }

    private static RegionFaction AddPresence(Region region, Faction faction)
    {
        RegionFaction presence = new(new PlanetFaction(faction), region);
        region.RegionFactionMap[faction.Id] = presence;
        return presence;
    }

    private static Squad AddDefender(RegionFaction presence, string name)
    {
        Squad squad = TestModelFactory.CreateSquad(name, TestModelFactory.CreateSoldier());
        squad.CurrentOrders = new Order([squad], true, false,
            Aggression.Cautious, new Mission(MissionType.DefenseInDepth, presence, 0));
        presence.LandedSquads.Add(squad);
        return squad;
    }

    private static Faction CreateFaction(int id, string name, bool isPlayer = false, bool isDefault = false) =>
        new(id, name, Color.White, isPlayer, isDefault, FactionBehavior.None, GrowthType.Logistic,
            new Dictionary<int, OnlyWar.Models.Soldiers.Species>(),
            new Dictionary<int, OnlyWar.Models.Soldiers.SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, OnlyWar.Models.Units.UnitTemplate>(),
            new Dictionary<int, OnlyWar.Models.Fleets.BoatTemplate>(),
            new Dictionary<int, OnlyWar.Models.Fleets.ShipTemplate>(),
            new Dictionary<int, OnlyWar.Models.Fleets.FleetTemplate>());
}
