using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Missions.Assault;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using System.Collections.Generic;
using System.Drawing;
using Xunit;

namespace OnlyWar.Tests.Missions;

public class ReciprocalAssaultCoordinatorTests
{
    [Fact]
    public void ExactMutualTargetsInOneRegionAreReciprocal()
    {
        Fixture fixture = CreateFixture(secondStartsInBeta: false);

        Assert.True(MissionTurnProcessor.AreReciprocalAssaults(
            fixture.First.State, fixture.Second.State));
    }

    [Fact]
    public void InboundCounterAssaultDoesNotInterruptUntilAfterApproachDay()
    {
        Fixture fixture = CreateFixture(secondStartsInBeta: true);
        int meetings = 0;

        MissionTurnProcessor.ResolveReciprocalAssaults(
            [fixture.First, fixture.Second], day: 1,
            (_, _) => meetings++);

        Assert.Equal(0, meetings);

        // Stand in for the local force's ordinary Day-1 assault on the defenders. The inbound
        // force actually runs its approach step, which leaves Prepare Assault queued for Day 2.
        fixture.First.State.DaysElapsed = 1;
        fixture.Second.AdvanceOneStep();

        MissionTurnProcessor.ResolveReciprocalAssaults(
            [fixture.First, fixture.Second], day: 2,
            (_, _) => meetings++);

        Assert.Equal(1, meetings);
    }

    [Fact]
    public void DefeatedParticipantCompletesWhileWinnerKeepsOrdinaryAssaultQueued()
    {
        Fixture fixture = CreateFixture(secondStartsInBeta: false);

        MissionTurnProcessor.ResolveReciprocalAssaults(
            [fixture.First, fixture.Second], day: 1,
            (winner, loser) =>
            {
                winner.State.DaysElapsed++;
                loser.State.DaysElapsed++;
                loser.Complete();
            });

        Assert.False(fixture.First.IsComplete);
        Assert.IsType<PrepareAssaultMissionStep>(fixture.First.NextStep);
        Assert.True(fixture.Second.IsComplete);
        // Prepare Assault consumes a day, so the surviving force cannot press the static position
        // during the meeting-engagement day; the scheduler will first run it on Day 2.
        Assert.Equal(1, fixture.First.State.DaysElapsed);
    }

    [Fact]
    public void OperationalForceCanContestTomorrowRegardlessOfDailyWithdrawal()
    {
        Fixture fixture = CreateFixture(secondStartsInBeta: false);
        BattleHistory dailyBattle = new()
        {
            Outcome = new BattleOutcome(
                BattleEndReason.Withdrawal,
                BattleSide.Opposing)
        };

        fixture.First.State.RecordReciprocalAssaultOutcome(
            dailyBattle, BattleSide.Attacker, enemyDeaths: 0);

        Assert.False(fixture.First.State.ForceWithdrewUnderFire);
        Assert.True(ReciprocalAssaultResolver.CanContestTomorrow(
            fixture.First.State));
    }

    [Fact]
    public void SharedMeetingEngagementConsumesOneDayAndDoesNotDamageStaticGarrisons()
    {
        Fixture fixture = CreateFixture(secondStartsInBeta: false);

        ReciprocalAssaultResolver.ResolveDay(fixture.First, fixture.Second);

        Assert.Equal(1, fixture.First.State.DaysElapsed);
        Assert.Equal(1, fixture.Second.State.DaysElapsed);
        Assert.Contains(fixture.First.State.DebriefLines, line => line.HasBattle);
        Assert.Contains(fixture.Second.State.DebriefLines, line => line.HasBattle);
        Assert.Contains(fixture.First.State.Log, line =>
            line.Contains("neither side can use entrenchments"));
        Assert.Equal(0, fixture.First.State.DefenderBattleValueDestroyed);
        Assert.Equal(0, fixture.Second.State.DefenderBattleValueDestroyed);
    }

    private static Fixture CreateFixture(bool secondStartsInBeta)
    {
        Planet planet = new(1, "Test World", new Coordinate(0, 0), 1, null, 0, 0);
        Region alpha = new(1, planet, 0, "Alpha", new RegionCoordinate(0, 0), 0);
        Region beta = new(2, planet, 0, "Beta", new RegionCoordinate(1, 0), 0);
        planet.Regions[0] = alpha;
        planet.Regions[1] = beta;

        Faction firstFaction = SectorSimulationFixture.BuildTestFaction(
            10, "Faction X", isPlayer: true, isDefault: false);
        Faction secondFaction = SectorSimulationFixture.BuildTestFaction(
            20, "Faction Y", isPlayer: false, isDefault: false);
        PlanetFaction firstPlanetFaction = new(firstFaction) { IsPublic = true };
        PlanetFaction secondPlanetFaction = new(secondFaction) { IsPublic = true };
        planet.PlanetFactionMap[firstFaction.Id] = firstPlanetFaction;
        planet.PlanetFactionMap[secondFaction.Id] = secondPlanetFaction;
        RegionFaction firstPresence = new(firstPlanetFaction, alpha) { IsPublic = true };
        RegionFaction secondPresence = new(secondPlanetFaction, alpha) { IsPublic = true };
        alpha.RegionFactionMap[firstFaction.Id] = firstPresence;
        alpha.RegionFactionMap[secondFaction.Id] = secondPresence;

        Squad firstSquad = CreateSquad(firstFaction, "X Assault");
        Squad secondSquad = CreateSquad(secondFaction, "Y Assault");
        firstSquad.CurrentRegion = alpha;
        secondSquad.CurrentRegion = secondStartsInBeta ? beta : alpha;
        Order firstOrder = new(
            [firstSquad], true, true, Aggression.Aggressive,
            new Mission(MissionType.Advance, secondPresence, 0));
        Order secondOrder = new(
            [secondSquad], true, true, Aggression.Aggressive,
            new Mission(MissionType.Advance, firstPresence, 0));
        MissionContext firstContext = new(
            firstOrder, [new BattleSquad(true, firstSquad)], []);
        MissionContext secondContext = new(
            secondOrder, [new BattleSquad(false, secondSquad)], []);

        MissionStepDriver firstDriver = new(
            TestExecutionContextFactory.CreateMission(firstContext, new FixedRNG()),
            new PrepareAssaultMissionStep());
        IMissionStep secondStart = secondStartsInBeta
            ? new ApproachStub()
            : new PrepareAssaultMissionStep();
        MissionStepDriver secondDriver = new(
            TestExecutionContextFactory.CreateMission(secondContext, new FixedRNG()),
            secondStart);
        return new Fixture(firstDriver, secondDriver);
    }

    /// <summary>
    /// The shared <see cref="TestModelFactory.DefaultWeapons"/> knife with accuracy, which these
    /// squads need in order for their meeting engagement to END.
    ///
    /// <para>MeleeAttackAction forms <c>margin = (attackSkill + accuracy - movePenalty) -
    /// (defenderSkill + evasion + parry)</c> and hits only on <c>margin &gt; 0</c>. Both squads here
    /// are built from the same template, so every term cancels and the shared knife's accuracy of 0
    /// leaves the margin at EXACTLY 0 -- a miss. <see cref="FixedRNG"/> returns a constant 0.0 for
    /// every draw, so there is no variance to break that tie, and the two troopers missed each
    /// other identically for 1000 turns until the resolver's turn cap forced a disengagement
    /// (2026-08-09).</para>
    ///
    /// <para>3, not 1, so the margin survives <c>MeleeAttackAction.MovementAttackPenalty</c> (2)
    /// that a charging or repositioning attacker pays. Fixed in WEAPON DATA rather than by seeding
    /// the RNG deliberately: FixedRNG's determinism is worth keeping, and a decisive mean margin
    /// stays correct under engine changes that shift how many draws are consumed, where a seeded
    /// stream would silently reshuffle outcomes. Local to this file rather than applied to the
    /// shared weapon set because BattleTurnResolverWithdrawalTests' matched-speed stern chase
    /// requires the opposite property -- a pursuer that CANNOT settle the fight by killing.</para>
    /// </summary>
    private static readonly WeaponSet DecisiveMeleeWeapons = new(
        3,
        "Test Weapons (decisive melee)",
        primaryRanged: TestModelFactory.DefaultWeapons.PrimaryRangedWeapon,
        primaryMelee: new MeleeWeaponTemplate(
            4,
            "Accurate Test Knife",
            EquipLocation.OneHand,
            TestSkills.Melee,
            3,
            1,
            1,
            0,
            1,
            0,
            1));

    private static Squad CreateSquad(Faction faction, string name)
    {
        SquadTemplate template = new(
            faction.Id,
            $"{faction.Name} Test Squad",
            DecisiveMeleeWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.MarineTemplate, 1, 1)],
            SquadTypes.None)
        {
            Faction = faction
        };
        Squad squad = new(name, null, template);
        squad.AddSquadMember(TestModelFactory.CreateSoldier(name: $"{name} Trooper"));
        return squad;
    }

    private sealed class ApproachStub : IMissionStep
    {
        public string Description => "Approach";
        public bool ConsumesDay => true;

        public MissionStepResult ExecuteMissionStep(
            MissionExecutionContext execution,
            float marginOfSuccess,
            IMissionStep resumeStep)
        {
            execution.State.DaysElapsed++;
            return MissionStepResult.Continue(new PrepareAssaultMissionStep());
        }
    }

    private sealed record Fixture(
        MissionStepDriver First,
        MissionStepDriver Second);
}
