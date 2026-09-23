using System;
using System.Collections.Generic;
using System.Drawing;
using OnlyWar.Battles;
using OnlyWar.Battles.Aftermath;
using OnlyWar.Battles.Actions;
using OnlyWar.Battles.Models;
using OnlyWar.Domain;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class InterceptionEstimateFixturesTests
{
    [Fact]
    public void PairProjectionDoesNotCombineNearestGeometryWithUnrelatedSpeedExtrema()
    {
        var inputs = new[]
        {
            new BattleInterceptionProjection.PairInput(
                PursuerSquadId: 101,
                QuarrySquadId: 201,
                PursuerPosition: new(0, 0),
                QuarryPosition: new(8, 0),
                PursuerMoveSpeed: 4,
                QuarryMoveSpeed: 8,
                QuarryHeading: new(1, 0)),
            new BattleInterceptionProjection.PairInput(
                PursuerSquadId: 102,
                QuarrySquadId: 202,
                PursuerPosition: new(0, 100),
                QuarryPosition: new(0, 300),
                PursuerMoveSpeed: 10,
                QuarryMoveSpeed: 3,
                QuarryHeading: new(0, 1))
        };

        BattleInterceptionProjection.AggregateResult result =
            BattleInterceptionProjection.EarliestPair(inputs);

        Assert.Equal(102, result.PursuerSquadId);
        Assert.Equal(202, result.QuarrySquadId);
        Assert.Equal(102, result.SelectedInput?.PursuerSquadId);
        Assert.Equal(202, result.SelectedInput?.QuarrySquadId);
        Assert.Equal("press_reaches_contact", result.SelectedPair?.Reason);
        Assert.Equal((200f - BattleContactRules.MeleeContactAllowance) / 7f,
            result.EarliestContactTurns,
            precision: 4);
        // The old synthetic combination would have used the 8-yard geometry with 10 - 3 speed,
        // despite neither value belonging to that pair.
        Assert.NotEqual(7f / 7f, result.EarliestContactTurns, precision: 4);
    }

    [Fact]
    public void PairProjectionHandlesContactOrderAndDegenerateGeometry()
    {
        BattleInterceptionProjection.PairResult alreadyContact =
            BattleInterceptionProjection.Project(new(
                301, 401, new(0, 0), new(1, 0), 0, 0, new(1, 0)));
        Assert.Equal(0, alreadyContact.ContactTurns);
        Assert.Equal(0, alreadyContact.AttackableContactTurns);
        Assert.True(alreadyContact.CanReachContactThisTurn);

        BattleInterceptionProjection.PairResult contactDuringMovement =
            BattleInterceptionProjection.Project(new(
                302, 402, new(0, 0), new(2, 0), 2, 0, new(1, 0)));
        Assert.Equal(0.5f, contactDuringMovement.ContactTurns, precision: 4);
        Assert.Equal(1, contactDuringMovement.AttackableContactTurns);
        Assert.True(contactDuringMovement.CanReachContactThisTurn);

        BattleInterceptionProjection.PairResult noRelativeMotion =
            BattleInterceptionProjection.Project(new(
                303, 403, new(0, 0), new(20, 0), 6, 6, new(1, 0)));
        Assert.True(float.IsPositiveInfinity(noRelativeMotion.ContactTurns));
        Assert.False(noRelativeMotion.CanReachContactThisTurn);

        BattleInterceptionProjection.PairResult degenerate =
            BattleInterceptionProjection.Project(new(
                304, 404, new(0, 0), new(20, 0), 0, 0, new(0, 0)));
        Assert.True(float.IsPositiveInfinity(degenerate.ContactTurns));
        Assert.Equal("no_feasible_closing_path", degenerate.Reason);
    }

    [Fact]
    public void PairProjectionCanCatchACrossingQuarryAtEqualSpeed()
    {
        BattleInterceptionProjection.PairResult result =
            BattleInterceptionProjection.Project(new(
                305,
                405,
                new(0, 0),
                new(1.4f, 0),
                5,
                5,
                new(0, 1)));

        Assert.InRange(result.ContactTurns, 0.09f, 0.1f);
        Assert.Equal(1, result.AttackableContactTurns);
    }

    [Fact]
    public void PairProjectionReportsAnUnreachableSlowerPursuer()
    {
        BattleInterceptionProjection.PairResult result =
            BattleInterceptionProjection.Project(new(
                306,
                406,
                new(0, 0),
                new(20, 0),
                5,
                6,
                new(1, 0)));

        Assert.True(float.IsPositiveInfinity(result.ContactTurns));
        Assert.True(float.IsPositiveInfinity(result.AttackableContactTurns));
        Assert.False(result.CanReachContactThisTurn);
        Assert.Equal("moving_apart", result.Reason);
    }

    [Fact]
    public void NearestPursuerAndQuarryAreNotForceWideFastestAndSlowest()
    {
        var nearestPursuer = InterceptionEstimateFixture.SingleSoldierSquad(
            squadId: 101,
            soldierId: 1011,
            x: 0,
            y: 0,
            currentSpeed: 4);
        var fastestPursuer = InterceptionEstimateFixture.SingleSoldierSquad(
            squadId: 102,
            soldierId: 1021,
            x: 0,
            y: 20,
            currentSpeed: 10);
        var fastQuarry = InterceptionEstimateFixture.SingleSoldierSquad(
            squadId: 201,
            soldierId: 2011,
            x: 8,
            y: 0,
            currentSpeed: 8);
        var slowQuarry = InterceptionEstimateFixture.SingleSoldierSquad(
            squadId: 202,
            soldierId: 2021,
            x: 3,
            y: 20,
            currentSpeed: 3);

        var pursuers = new[] { nearestPursuer, fastestPursuer };
        var quarries = new[] { fastQuarry, slowQuarry };

        Assert.Equal(nearestPursuer, InterceptionEstimateFixture.NearestPursuer(pursuers, fastQuarry));
        Assert.Equal(slowQuarry, InterceptionEstimateFixture.NearestQuarry(quarries, fastestPursuer));
        Assert.Equal(10, InterceptionEstimateFixture.MinimumCurrentSpeed(fastestPursuer));
        Assert.Equal(3, InterceptionEstimateFixture.MinimumCurrentSpeed(slowQuarry));
        var mixedSpeedPursuer = new InterceptionEstimateFixture.GeometrySquad(
            103,
            new InterceptionEstimateFixture.GeometrySoldier[]
            {
                new(1031, 40, 40, 10),
                new(1032, 41, 40, 4)
            });
        Assert.Equal(4, InterceptionEstimateFixture.MinimumCurrentSpeed(mixedSpeedPursuer));

        // The force-wide minimum separation (3) and force-wide speed extremes (10 and 3)
        // make a finite number even though the nearest assigned pair has no positive closing
        // speed. This is the intentionally exposed mixed-input failure mode for the audit.
        float forceWideSeparation = MathF.Min(
            InterceptionEstimateFixture.NearestSoldierSeparation(nearestPursuer, fastQuarry),
            InterceptionEstimateFixture.NearestSoldierSeparation(fastestPursuer, slowQuarry));
        float forceWideEstimate = (forceWideSeparation - 1f)
            / (InterceptionEstimateFixture.MinimumCurrentSpeed(fastestPursuer)
                - InterceptionEstimateFixture.MinimumCurrentSpeed(slowQuarry));

        Assert.Equal(2f / 7f, forceWideEstimate, precision: 4);
        Assert.True(float.IsPositiveInfinity(
            InterceptionEstimateFixture.HypotheticalMeleeContactTurns(nearestPursuer, fastQuarry)));
    }

    [Fact]
    public void SquadCentroidDistanceIsNotNearestSoldierSeparation()
    {
        var pursuer = new InterceptionEstimateFixture.GeometrySquad(
            111,
            new InterceptionEstimateFixture.GeometrySoldier[]
            {
                new(1111, 0, 0, 8),
                new(1112, 0, 100, 8)
            });
        var quarry = new InterceptionEstimateFixture.GeometrySquad(
            211,
            new InterceptionEstimateFixture.GeometrySoldier[]
            {
                new(2111, 10, 0, 6),
                new(2112, 100, 100, 6)
            });

        Assert.Equal(
            10f,
            InterceptionEstimateFixture.NearestSoldierSeparation(pursuer, quarry),
            precision: 4);
        Assert.Equal(
            55f,
            InterceptionEstimateFixture.CentroidSeparation(pursuer, quarry),
            precision: 4);
    }

    [Fact]
    public void EqualAndSlowerPursuersHaveNoFinitePairwiseInterceptionTime()
    {
        var equalSpeedPursuer = InterceptionEstimateFixture.SingleSoldierSquad(
            squadId: 301,
            soldierId: 3011,
            x: 0,
            y: 0,
            currentSpeed: 6);
        var equalSpeedQuarry = InterceptionEstimateFixture.SingleSoldierSquad(
            squadId: 302,
            soldierId: 3021,
            x: 20,
            y: 0,
            currentSpeed: 6);
        var slowerPursuer = InterceptionEstimateFixture.SingleSoldierSquad(
            squadId: 303,
            soldierId: 3031,
            x: 0,
            y: 0,
            currentSpeed: 5);
        var fasterQuarry = InterceptionEstimateFixture.SingleSoldierSquad(
            squadId: 304,
            soldierId: 3041,
            x: 20,
            y: 0,
            currentSpeed: 6);
        var fasterPursuer = InterceptionEstimateFixture.SingleSoldierSquad(
            squadId: 305,
            soldierId: 3051,
            x: 0,
            y: 0,
            currentSpeed: 8);

        Assert.True(float.IsPositiveInfinity(
            InterceptionEstimateFixture.HypotheticalMeleeContactTurns(
                equalSpeedPursuer,
                equalSpeedQuarry)));
        Assert.True(float.IsPositiveInfinity(
            InterceptionEstimateFixture.HypotheticalMeleeContactTurns(
                slowerPursuer,
                fasterQuarry)));
        Assert.Equal(
            9.5f,
            InterceptionEstimateFixture.HypotheticalMeleeContactTurns(
                fasterPursuer,
                equalSpeedQuarry),
            precision: 4);
        Assert.Equal(
            5f,
            InterceptionEstimateFixture.HypotheticalUsefulAttackTurns(
                fasterPursuer,
                equalSpeedQuarry,
                usefulAttackRange: 10),
            precision: 4);
    }

    [Fact]
    public void ObservedProgressIsSeparateFromALargeScalarInterceptionDiagnostic()
    {
        var startingPursuer = InterceptionEstimateFixture.SingleSoldierSquad(
            squadId: 401,
            soldierId: 4011,
            x: 0,
            y: 0,
            currentSpeed: 6.015f);
        var startingQuarry = InterceptionEstimateFixture.SingleSoldierSquad(
            squadId: 402,
            soldierId: 4021,
            x: 100,
            y: 0,
            currentSpeed: 6f);
        var endingPursuer = InterceptionEstimateFixture.SingleSoldierSquad(
            squadId: 401,
            soldierId: 4011,
            x: 1,
            y: 0,
            currentSpeed: 6.015f);
        var endingQuarry = InterceptionEstimateFixture.SingleSoldierSquad(
            squadId: 402,
            soldierId: 4021,
            x: 99,
            y: 0,
            currentSpeed: 6f);

        var progress = InterceptionEstimateFixture.Observe(
            startingPursuer,
            startingQuarry,
            endingPursuer,
            endingQuarry,
            pursuerDeclaredSpeed: 6.015f,
            quarryDeclaredSpeed: 6f);

        Assert.Equal(2f, progress.SeparationGain, precision: 4);
        Assert.Equal(0.015f, progress.ScalarClosingSpeed, precision: 4);
        Assert.InRange(progress.ScalarInterceptTurns!.Value, 6_400f, 6_600f);
        Assert.NotEqual(progress.SeparationGain, progress.ScalarInterceptTurns.Value);
    }

    [Fact]
    public void StraightChaseRetainsGridRoundingAndLeftoverMovement()
    {
        BattleGridManager grid = new();
        BattleSoldier pursuer = CreatePlacedSoldier(grid, id: 501, x: 0, y: 0);
        BattleSoldier quarry = CreatePlacedSoldier(grid, id: 502, x: 20, y: 0);
        SoldierMovementProjector projector = new(grid);

        ExecuteProjectedMove(grid, projector, pursuer, (1, 0), 3.6f);
        ExecuteProjectedMove(grid, projector, quarry, (-1, 0), 2.6f);
        Assert.Equal((3, 0), pursuer.TopLeft);
        Assert.Equal((18, 0), quarry.TopLeft);
        Assert.Equal(0.6f, pursuer.LeftoverMovement, precision: 4);
        Assert.Equal(0.6f, quarry.LeftoverMovement, precision: 4);

        ExecuteProjectedMove(grid, projector, pursuer, (1, 0), 3.6f + pursuer.LeftoverMovement);
        ExecuteProjectedMove(grid, projector, quarry, (-1, 0), 2.6f + quarry.LeftoverMovement);
        Assert.Equal((7, 0), pursuer.TopLeft);
        Assert.Equal((15, 0), quarry.TopLeft);
        Assert.Equal(0.2f, pursuer.LeftoverMovement, precision: 4);
        Assert.Equal(0.2f, quarry.LeftoverMovement, precision: 4);

        ExecuteProjectedMove(grid, projector, pursuer, (1, 0), 3.6f + pursuer.LeftoverMovement);
        ExecuteProjectedMove(grid, projector, quarry, (-1, 0), 2.6f + quarry.LeftoverMovement);
        Assert.Equal((10, 0), pursuer.TopLeft);
        Assert.Equal((13, 0), quarry.TopLeft);
        Assert.Equal(3f, quarry.TopLeft.Value.Item1 - pursuer.TopLeft.Value.Item1);
    }

    [Fact]
    public void FasterMovementDoesNotImplyObservedSeparationGain()
    {
        BattleGridManager grid = new();
        BattleSoldier pursuer = CreatePlacedSoldier(grid, id: 601, x: 0, y: 0);
        BattleSoldier quarry = CreatePlacedSoldier(grid, id: 602, x: 10, y: 0);
        pursuer.CurrentSpeed = 10f;
        quarry.CurrentSpeed = 8f;
        float startSeparation = Distance(pursuer, quarry);

        MoveAction pursuerMove = new(
            pursuer,
            grid,
            pursuer.TopLeft.Value,
            new ValueTuple<int, int>(0, 8),
            orientation: 0,
            movementBudget: 8f);
        MoveAction quarryMove = new(
            quarry,
            grid,
            quarry.TopLeft.Value,
            new ValueTuple<int, int>(10, 8),
            orientation: 0,
            movementBudget: 8f);
        pursuerMove.Execute(state: null);
        quarryMove.Execute(state: null);

        Assert.True(pursuer.CurrentSpeed > quarry.CurrentSpeed);
        Assert.True(pursuerMove.Succeeded);
        Assert.True(quarryMove.Succeeded);
        Assert.Equal(startSeparation, Distance(pursuer, quarry), precision: 4);
    }

    [Fact]
    public void ShootingCanEndBattleBeforeHypotheticalMeleeContact()
    {
        BattleSquad attacker = CreateBattleSquad(
            CreateFaction(70_001, "Interception Shooters"),
            "Interception Shooters",
            70_101);
        BattleSquad defender = CreateBattleSquad(
            CreateFaction(70_002, "Interception Target"),
            "Interception Target",
            70_201);
        ((Soldier)defender.Soldiers[0].Soldier).MoveSpeed = 1;
        var meleeEstimate = InterceptionEstimateFixture.HypotheticalMeleeContactTurns(
            InterceptionEstimateFixture.SingleSoldierSquad(
                701,
                7011,
                0,
                0,
                attacker.Soldiers[0].GetMoveSpeed()),
            InterceptionEstimateFixture.SingleSoldierSquad(
                702,
                7021,
                10,
                0,
                defender.Soldiers[0].GetMoveSpeed()));
        Assert.Equal(1.8f, meleeEstimate, precision: 4);

        EquipOverkillRifle(attacker.Soldiers[0], 70_301);
        EquipOverkillRifle(defender.Soldiers[0], 70_302);

        BattleGridManager grid = new();
        Place(grid, attacker.Soldiers[0], side: true, x: 0, y: 0);
        Place(grid, defender.Soldiers[0], side: false, x: 10, y: 0);
        BattleTurnResolver resolver = CreateResolver(grid, attacker, defender);

        resolver.ProcessNextTurn();

        Assert.NotNull(resolver.BattleHistory.Outcome);
        BattleOutcome outcome = Assert.IsType<BattleOutcome>(resolver.BattleHistory.Outcome);
        Assert.Equal(BattleEndReason.Annihilation, outcome.EndReason);
        Assert.Contains(
            resolver.BattleHistory.Turns[1].Actions,
            action => action is ShootAction shot && shot.ShooterId == attacker.Soldiers[0].Soldier.Id);
        Assert.DoesNotContain(
            resolver.BattleHistory.Turns[1].Actions,
            action => action is MeleeAttackAction or SquadClosingMoveAction);
    }

    private static void ExecuteProjectedMove(
        BattleGridManager grid,
        SoldierMovementProjector projector,
        BattleSoldier soldier,
        ValueTuple<int, int> line,
        float movementBudget)
    {
        ValueTuple<int, int> delta = projector.CalculateMovementAlongLine(line, movementBudget);
        ValueTuple<int, int> current = soldier.TopLeft.Value;
        ValueTuple<int, int> destination = new(
            current.Item1 + delta.Item1,
            current.Item2 + delta.Item2);
        MoveAction action = new(
            soldier,
            grid,
            current,
            destination,
            orientation: 0,
            movementBudget);
        action.Execute(state: null);
        Assert.True(action.Succeeded);
    }

    private static BattleSoldier CreatePlacedSoldier(BattleGridManager grid, int id, int x, int y)
    {
        var model = TestModelFactory.CreateSoldier(name: $"Interception Fixture {id}");
        model.Id = id;
        BattleSoldier soldier = new(model, squad: null)
        {
            TopLeft = new ValueTuple<int, int>(x, y),
            Orientation = 0
        };
        grid.PlaceSoldier(soldier, side: true, [soldier.TopLeft.Value]);
        return soldier;
    }

    private static BattleTurnResolver CreateResolver(
        BattleGridManager grid,
        BattleSquad attacker,
        BattleSquad defender)
    {
        GameRulesData rules = OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(
            RulesDatabaseFixture.DatabasePath);
        Date battleDate = new(1, 1, 1);
        string originalDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = RulesDatabaseFixture.RepositoryRoot;
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }

        RNG.Reset(70_000);
        StaticRNG random = new();
        BattleAftermathDependencies aftermath = new(
            battleDate,
            random,
            NoOpPlayerBattleAftermathSink.Instance);
        BattleExecutionContext execution = new(rules, random, aftermath);
        return new BattleTurnResolver(
            grid,
            [attacker],
            [defender],
            region: null,
            execution,
            new BattleSideProfile(Aggression.Normal, BattleRole.AssassinationAttacker),
            new BattleSideProfile(Aggression.Normal, BattleRole.Defender));
    }

    private static BattleSquad CreateBattleSquad(Faction faction, string name, int soldierId)
    {
        SquadTemplate template = new(
            soldierId,
            $"{name} Template",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 1)],
            SquadTypes.None)
        {
            Faction = faction
        };
        Soldier soldier = TestModelFactory.CreateSoldier(name: $"{name} Soldier");
        soldier.Id = soldierId;
        Squad squad = new(name, null, template);
        squad.AddSquadMember(soldier);
        return new BattleSquad(false, squad);
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
            GrowthType.None,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate> { [TestModelFactory.MarineTemplate.Id] = TestModelFactory.MarineTemplate },
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, OnlyWar.Domain.Units.UnitTemplate>(),
            new Dictionary<int, OnlyWar.Domain.Fleets.BoatTemplate>(),
            new Dictionary<int, OnlyWar.Domain.Fleets.ShipTemplate>(),
            new Dictionary<int, OnlyWar.Domain.Fleets.FleetTemplate>());
    }

    private static void Place(BattleGridManager grid, BattleSoldier soldier, bool side, int x, int y)
    {
        soldier.TopLeft = new ValueTuple<int, int>(x, y);
        grid.PlaceSoldier(soldier, side, [soldier.TopLeft.Value]);
    }

    private static void EquipOverkillRifle(BattleSoldier soldier, int templateId)
    {
        ((Soldier)soldier.Soldier).Dexterity = 20;
        RangedWeapon rifle = new(new RangedWeaponTemplate(
            templateId,
            "Interception Fixture Rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 6,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 1_000,
            maxDistance: 100,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 4,
            doesDamageDegradeWithRange: false,
            reloadTime: 1));
        soldier.RangedWeapons.Clear();
        soldier.ClearReadiedRangedWeapons();
        soldier.RangedWeapons.Add(rifle);
        soldier.ReadyWeapon(rifle);
    }

    private static float Distance(BattleSoldier first, BattleSoldier second)
    {
        return MathF.Sqrt(
            MathF.Pow(first.TopLeft.Value.Item1 - second.TopLeft.Value.Item1, 2f)
            + MathF.Pow(first.TopLeft.Value.Item2 - second.TopLeft.Value.Item2, 2f));
    }

    private sealed class NoOpPlayerBattleAftermathSink : IPlayerBattleAftermathSink
    {
        internal static NoOpPlayerBattleAftermathSink Instance { get; } = new();

        public void MoveToFallenBrothers(PlayerSoldier soldier)
        {
        }

        public void AddRecoveredGeneseed(float purity)
        {
        }

        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents)
        {
        }
    }
}
