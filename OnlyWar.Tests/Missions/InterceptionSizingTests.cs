using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Missions.Recon;
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

// DetectedMissionStep sizes an interception as
//
//     intruderBattleValue * (InterceptionBaseMultiple + |stealthMargin| * InterceptionMultiplePerSigma)
//
// and fulfils it from the region's Patrol/Recon squads largest-first, stopping as soon as the
// requirement is met. The per-sigma term is the half of that formula that says "the worse you were
// seen, the more of the screen turns up," and it was unexercised by the suite
// (OnlyWar_TDD.md §6.4): every other interception case has a screen
// of exactly ONE squad, which commits identically at every margin, so the multiple could have been
// any number at all and nothing would have noticed.
//
// These fixtures give the region several interchangeable squads, so the requirement is the only
// thing deciding how many of them scramble. Every test soldier carries battleValue 2, so a two-man
// squad is 4 battle value and the arithmetic stays legible in the assertions.
public class InterceptionSizingTests
{
    private const int SquadBattleValue = 4;

    // The load-bearing behaviour: a marginal detection pulls one squad off the screen and leaves the
    // rest sweeping; a thoroughly blown one pulls most of the region's screen onto the intruder. Both
    // the count committed and the fact that the REST keep screening are consequences of the multiple.
    [Theory]
    // required 4 BV: one squad already covers it.
    [InlineData(0.0f, 1)]
    // required 6 BV: one squad (4) falls short, so a second joins - a partial sigma still costs a
    // whole squad, because squads are the granularity the screen commits in.
    [InlineData(-1.0f, 2)]
    // required 8 BV: exactly two.
    [InlineData(-2.0f, 2)]
    // required 12 BV: three.
    [InlineData(-4.0f, 3)]
    public void Interception_CommitsMoreOfTheScreen_TheWorseTheIntruderWasSeen(
        float stealthMargin, int expectedSquads)
    {
        MissionContext context = CreateDetectionScenario(screenSquads: 4);

        new DetectedMissionStep().ExecuteMissionStep(
            CreateExecution(context), stealthMargin, resumeStep: null);

        Assert.Equal(expectedSquads, context.OpposingSquads.Count);
        Assert.Equal(expectedSquads * SquadBattleValue, CommittedBattleValue(context));
    }

    // The same claim stated without the constant baked in, so that retuning
    // InterceptionMultiplePerSigma does not silently invert the mechanic while the Theory above is
    // being updated to match. A worse margin must never commit LESS.
    [Fact]
    public void Interception_ABlownStealthCheck_CommitsStrictlyMoreThanACleanOne()
    {
        MissionContext clean = CreateDetectionScenario(screenSquads: 4);
        MissionContext blown = CreateDetectionScenario(screenSquads: 4);

        new DetectedMissionStep().ExecuteMissionStep(CreateExecution(clean), 0.0f, resumeStep: null);
        new DetectedMissionStep().ExecuteMissionStep(CreateExecution(blown), -4.0f, resumeStep: null);

        Assert.True(
            CommittedBattleValue(blown) > CommittedBattleValue(clean),
            $"a blown detection committed {CommittedBattleValue(blown)} BV, no more than the "
            + $"{CommittedBattleValue(clean)} BV a clean one committed");
    }

    // The multiple is what the screen WANTS, not a gate on engaging. A lone squad at bare parity with
    // the intruder still goes in when the requirement is three times what it has - the below-parity
    // refusal is a separate rule, measured against the intruder rather than against the requirement.
    // Getting this wrong would quietly restore the old "detection always summons a competent response"
    // behaviour in reverse: a thin screen that never engages at all.
    [Fact]
    public void Interception_AThinScreenAtParity_EngagesWithLessThanItWanted()
    {
        MissionContext context = CreateDetectionScenario(screenSquads: 1);

        new DetectedMissionStep().ExecuteMissionStep(
            CreateExecution(context), -4.0f, resumeStep: null);

        Assert.Single(context.OpposingSquads);
        Assert.Contains(context.Log, line => line.Contains("detected and intercepted"));
    }

    [Fact]
    public void Interception_CombatIneffectiveIntruder_EndsWithoutBuildingEmptyInterception()
    {
        MissionContext context = CreateDetectionScenario(screenSquads: 0);
        BattleSquad intruder = Assert.Single(context.MissionSquads);
        foreach (BattleSoldier soldier in intruder.Soldiers.ToList())
        {
            intruder.RemoveSoldier(soldier);
        }

        MissionStepResult result = new DetectedMissionStep().ExecuteMissionStep(
            CreateExecution(context), -1.0f, resumeStep: null);

        Assert.Null(result.Next);
        Assert.True(context.ObjectiveAborted);
        Assert.Empty(context.OpposingSquads);
        Assert.Contains(
            context.Log,
            line => line.Contains("no combat-capable mission force remained"));
    }

    // --- fixtures ---

    private static int CommittedBattleValue(MissionContext context) =>
        context.OpposingSquads
            .SelectMany(squad => squad.AbleSoldiers)
            .Sum(soldier => soldier.Soldier.Template.BattleValue);

    // A two-man intruder (4 battle value) caught in a region held by one enemy faction, with
    // `screenSquads` interchangeable two-man patrols out looking. Spotter is left unset so the step
    // falls back to the mission's own target RegionFaction, which is where the screen lives.
    private static MissionContext CreateDetectionScenario(int screenSquads)
    {
        Planet planet = new(1, "Test Planet", new Coordinate(0, 0), 1, null, 0, 0);
        Region region = new(1, planet, 0, "Target Region", new RegionCoordinate(0, 0), 0);
        planet.Regions[0] = region;

        Faction defenderFaction = CreateFaction(20, "Swarm");
        PlanetFaction planetFaction = new(defenderFaction) { IsPublic = true };
        RegionFaction defender = new(planetFaction, region)
        {
            Population = 10_000,
            IsPublic = true
        };
        region.RegionFactionMap[defenderFaction.Id] = defender;

        for (int i = 0; i < screenSquads; i++)
        {
            SendOnPatrol(defender, $"Screen {i}");
        }

        Squad intruder = TestModelFactory.CreateSquad(
            "Scout Squad",
            TestModelFactory.CreateSoldier(TestModelFactory.SergeantTemplate, "Scout Sergeant"),
            TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate, "Scout"));
        intruder.CurrentRegion = region;
        Order order = new(
            [intruder],
            isQuiet: true,
            isActivelyEngaging: false,
            levelOfAggression: Aggression.Normal,
            mission: new Mission(MissionType.Recon, defender, missionSize: 0));

        return new MissionContext(order, [new BattleSquad(true, intruder)], []);
    }

    // A two-man patrol landed in the region. The Order constructor is what wires Squad.CurrentOrders
    // back to the order, which is the link DetectedMissionStep filters the screen on; constructing it
    // is not a no-op even though the result is unused.
    private static void SendOnPatrol(RegionFaction regionFaction, string name)
    {
        Squad squad = TestModelFactory.CreateSquad(
            name,
            TestModelFactory.CreateSoldier(name: $"{name} 0"),
            TestModelFactory.CreateSoldier(name: $"{name} 1"));
        _ = new Order(
            [squad],
            isQuiet: false,
            isActivelyEngaging: false,
            levelOfAggression: Aggression.Normal,
            mission: new Mission(MissionType.Patrol, regionFaction, missionSize: 0));
        squad.CurrentRegion = regionFaction.Region;
        regionFaction.LandedSquads.Add(squad);
    }

    private static MissionExecutionContext CreateExecution(MissionContext context) =>
        TestExecutionContextFactory.CreateMission(context, new FixedRNG());

    private static Faction CreateFaction(int id, string name) =>
        new(
            id,
            name,
            Color.Red,
            isPlayerFaction: false,
            isDefaultFaction: false,
            behavior: FactionBehavior.None,
            GrowthType.Conversion,
            new Dictionary<int, Species>(),
            new Dictionary<int, SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
}
