using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Missions.Ambush;
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

public class MissionDayBudgetTests
{
    // A force that infiltrated owes the last day of the week to getting back out, so it stops
    // operating after day 6. Every re-entry into an operating step must honor this, not just the
    // successful-recon branch: an intercepted force that stays on mission comes back from the
    // engagement into the stealth step, and used to get day 7 as another scouting day - which
    // pushed exfiltration to day 8 or later.
    [Theory]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    public void InfiltratedForce_StopsOperatingAfterDaySix(int daysElapsed, bool expected)
    {
        MissionContext context = CreateContext(squadIsInTargetRegion: false);
        context.DaysElapsed = (ushort)daysElapsed;

        Assert.True(context.MustExfiltrate);
        Assert.Equal(expected, context.OperatingDaysSpent);
    }

    // A force operating in a region it already stands in has no trip home to budget for, so it
    // works the full week.
    [Theory]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, true)]
    public void ForceInItsOwnRegion_OperatesTheFullWeek(int daysElapsed, bool expected)
    {
        MissionContext context = CreateContext(squadIsInTargetRegion: true);
        context.DaysElapsed = (ushort)daysElapsed;

        Assert.False(context.MustExfiltrate);
        Assert.Equal(expected, context.OperatingDaysSpent);
    }

    // A successful sabotage is a single completed objective. The six unused days are left available
    // for training rather than being spent repeatedly damaging the same target.
    [Fact]
    public void SuccessfulSabotageInOwnRegion_StopsAfterOneStrike()
    {
        MissionContext context = CreateSabotageContext(squadIsInTargetRegion: true);

        new MissionStepDriver(CreateExecution(context), new SabotageStealthMissionStep()).RunToCompletion();

        Assert.Equal(1, context.DaysElapsed);
        Assert.Single(context.Log, line => line.Contains("plants explosives"));
        Assert.True(context.Impact > 0);
        Assert.DoesNotContain(context.Log, line => line.Contains("reconnaissance"));
    }

    // An infiltrated sabotage force strikes once and then immediately spends the next day returning
    // to base. The remaining five days are therefore available for training.
    [Fact]
    public void SuccessfulInfiltratedSabotage_StrikesOnceThenExfiltrates()
    {
        MissionContext context = CreateSabotageContext(squadIsInTargetRegion: false);

        new MissionStepDriver(CreateExecution(context), new SabotageStealthMissionStep()).RunToCompletion();

        Assert.Equal(2, context.DaysElapsed);
        Assert.Single(context.Log, line => line.Contains("plants explosives"));
        Assert.True(context.Impact > 0);
        Assert.Contains(context.Log, line => line.Contains("returned to base"));
        Assert.DoesNotContain(context.Log, line => line.Contains("reconnaissance"));
    }

    [Fact]
    public void SuccessfulExfiltration_IsLabeledAndRecordedAsReturnToBase()
    {
        MissionContext context = CreateSabotageContext(squadIsInTargetRegion: false);
        ExfiltrateMissionStep step = new();

        new MissionStepDriver(CreateExecution(context), step).RunToCompletion();

        Assert.Equal("Exfiltrate", step.Description);
        Assert.True(context.ForceReturnedToBase);
        Assert.True(MissionOutcomeClassifier.Classify(context).ReturnedToBase);
        Assert.Contains(context.Log, line => line.Contains("returned to base"));
    }

    [Fact]
    public void ExfiltrationGraceExpired_DeploysForceInTargetRegionForNextTurn()
    {
        MissionContext context = CreateSabotageContext(squadIsInTargetRegion: false);
        Region target = context.Order.Mission.RegionFaction.Region;
        context.DaysElapsed = MissionContext.MissionDurationDays
            + MissionContext.ExfiltrationGraceDays;

        new MissionStepDriver(CreateExecution(context), new ExfiltrateMissionStep()).RunToCompletion();

        Assert.False(context.ForceLostContact);
        Assert.True(context.ForceRemainedInTargetRegion);
        Assert.All(context.MissionSquads, squad => Assert.Same(target, squad.Squad.CurrentRegion));
        RegionFaction deployedPresence = target.RegionFactionMap[
            context.MissionSquads[0].Squad.Faction.Id];
        Assert.True(deployedPresence.IsPublic);
        Assert.All(context.MissionSquads,
            squad => Assert.Contains(squad.Squad, deployedPresence.LandedSquads));
        Assert.Contains(context.Log, line => line.Contains("remains deployed"));
    }

    [Fact]
    public void AmbushWithNoViableTarget_StillExfiltrates()
    {
        MissionContext context = CreateSabotageContext(
            squadIsInTargetRegion: false,
            missionType: MissionType.Ambush);

        new MissionStepDriver(CreateExecution(context), new PerformAmbushMissionStep()).RunToCompletion();

        Assert.True(context.NoViableTarget);
        Assert.True(context.ForceReturnedToBase);
        Assert.Contains(context.Log, line => line.Contains("returned to base"));
    }

    // SabotageStealthMissionStep is the sabotage loop's re-entry point - DetectedMissionStep is handed
    // it as the returnStep, so a force that was intercepted and survived comes back here rather than
    // through PerformSabotageMissionStep. It previously had no day check of its own, so that path had
    // no upper bound at all. Entering it with the budget already spent must break off, not spend
    // another day and roll stealth again.
    [Theory]
    [InlineData(true, 7)]
    [InlineData(true, 9)]
    [InlineData(false, 6)]
    [InlineData(false, 9)]
    public void SabotageStealth_ReenteredWithBudgetSpent_DoesNotSpendAnotherDay(
        bool squadIsInTargetRegion, int daysElapsed)
    {
        MissionContext context = CreateSabotageContext(squadIsInTargetRegion);
        context.DaysElapsed = (ushort)daysElapsed;

        new MissionStepDriver(CreateExecution(context), new SabotageStealthMissionStep()).RunToCompletion();

        Assert.DoesNotContain(context.Log, line => line.Contains("plants explosives"));
        // A force in its own region simply stops; one that has to exfiltrate spends its remaining
        // days getting out, and past the grace window is written off rather than looping forever.
        if (squadIsInTargetRegion)
        {
            Assert.Equal(daysElapsed, context.DaysElapsed);
            Assert.Empty(context.Log);
        }
        else
        {
            Assert.Contains(context.Log, line =>
                line.Contains("returned to base") || line.Contains("gone to ground"));
        }
    }

    // Squad skills are set well above the checks' difficulty and FixedRNG rolls a z of 0, so every
    // stealth/tactics check in the loop succeeds - the mission runs its full day budget without
    // diverting into DetectedMissionStep, which is what these tests are measuring.
    private static MissionContext CreateSabotageContext(
        bool squadIsInTargetRegion,
        MissionType missionType = MissionType.Sabotage)
    {
        Planet planet = new(1, "Test Planet", new Coordinate(0, 0), 1, null, 0, 0);
        Region targetRegion = CreateRegion(1, "Target Region", planet);
        Region stagingRegion = CreateRegion(2, "Staging Region", planet);
        planet.Regions[0] = targetRegion;
        Faction defender = CreateFaction(20, "Defenders");
        // Population must be set before Garrison; RegionFaction clamps Garrison to Population.
        RegionFaction targetFaction = new(new PlanetFaction(defender), targetRegion)
        {
            Population = 1000,
            Garrison = 100
        };
        targetRegion.RegionFactionMap[defender.Id] = targetFaction;

        Faction attacker = CreateFaction(10, "Attackers");
        SquadTemplate squadTemplate = new(
            10,
            "Saboteur Squad",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [
                new SquadTemplateElement(TestModelFactory.SergeantTemplate, 0, 1),
                new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 4)
            ],
            SquadTypes.None)
        {
            Faction = attacker
        };
        Squad squad = new("Saboteur Squad", null, squadTemplate);
        squad.AddSquadMember(CreateSaboteur(
            TestModelFactory.SergeantTemplate, "Saboteur Sergeant"));
        squad.AddSquadMember(CreateSaboteur(TestModelFactory.MarineTemplate, "Saboteur"));
        Region startingRegion = squadIsInTargetRegion ? targetRegion : stagingRegion;
        squad.CurrentRegion = startingRegion;
        PlanetFaction attackerPlanetPresence = new(attacker);
        planet.PlanetFactionMap[attacker.Id] = attackerPlanetPresence;
        RegionFaction attackerStartingPresence = new(attackerPlanetPresence, startingRegion);
        startingRegion.RegionFactionMap[attacker.Id] = attackerStartingPresence;
        attackerStartingPresence.LandedSquads.Add(squad);
        Order order = new(
            [squad],
            isQuiet: true,
            isActivelyEngaging: false,
            levelOfAggression: Aggression.Normal,
            mission: new Mission(missionType, targetFaction, missionSize: 0));

        return new MissionContext(order, [new BattleSquad(true, squad)], []);
    }

    private static Soldier CreateSaboteur(SoldierTemplate template, string name) =>
        TestModelFactory.CreateSoldier(
            template,
            name,
            skills: [new Skill(TestSkills.Stealth, 256), new Skill(TestSkills.Tactics, 256)]);

    private static MissionExecutionContext CreateExecution(MissionContext context) =>
        TestExecutionContextFactory.CreateMission(context, new FixedRNG());

    private static MissionContext CreateContext(bool squadIsInTargetRegion)
    {
        Region targetRegion = CreateRegion(1, "Target Region");
        Region stagingRegion = CreateRegion(2, "Staging Region");
        Faction defender = CreateFaction(20, "Defenders");
        RegionFaction targetFaction = new(new PlanetFaction(defender), targetRegion);
        targetRegion.RegionFactionMap[defender.Id] = targetFaction;

        Squad squad = TestModelFactory.CreateSquad(
            "Scout Squad",
            TestModelFactory.CreateSoldier(name: "Scout"));
        squad.CurrentRegion = squadIsInTargetRegion ? targetRegion : stagingRegion;
        Order order = new(
            [squad],
            isQuiet: true,
            isActivelyEngaging: false,
            levelOfAggression: Aggression.Normal,
            mission: new Mission(MissionType.Recon, targetFaction, missionSize: 0));

        return new MissionContext(order, [new BattleSquad(true, squad)], []);
    }

    // The day budget only compares region identity, so the property tests get by with no owning
    // planet; the tests that actually run mission steps pass one, because the steps' trace lines
    // name the planet.
    private static Region CreateRegion(int id, string name, Planet planet = null)
    {
        return new Region(id, planet, 0, name, new RegionCoordinate(0, 0), 0);
    }

    private static Faction CreateFaction(int id, string name)
    {
        return new Faction(
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
}
