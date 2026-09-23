using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Battles;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class AttackOpportunityProjectionTests
{
    [Fact]
    public void LoadedWeaponHasAConcreteRangedOpportunityNow()
    {
        ProjectionFixture fixture = CreateFixture(distance: 30);

        BattleAttackOpportunityProjection.PairResult result = fixture.Project();

        Assert.Equal(BattleAttackMode.Ranged, result.Ranged.Mode);
        Assert.Equal(BattleAttackPreparation.None, result.Ranged.Preparation);
        Assert.Equal(0, result.Ranged.ElapsedTurns);
        Assert.False(result.Ranged.RequiresMovement);
        Assert.Equal("shot_available_now", result.Ranged.Reason);
        Assert.Equal(fixture.Weapon.Template.Id, result.Ranged.WeaponTemplateId);
        Assert.Equal("useful_range", result.Ranged.DestinationCondition);
        Assert.Equal(0f, result.Ranged.Geometry?.PursuerPosition.X);
        Assert.Equal(30f, result.Ranged.Geometry?.QuarryPosition.X);
        Assert.Equal(8f, result.Ranged.Geometry?.PursuerMoveSpeed);
        Assert.Equal(0f, result.Ranged.Geometry?.QuarryMoveSpeed);
        Assert.Equal(BattleAttackMode.Ranged, result.Earliest.Mode);
    }

    [Fact]
    public void EmptyMagazineWithReserveRequiresReloadAndIsNotAmmunitionExhaustion()
    {
        ProjectionFixture fixture = CreateFixture(distance: 30, typedAmmunition: true);
        fixture.Weapon.LoadedAmmo = 0;
        fixture.Weapon.ReserveAmmo = fixture.Weapon.Template.AmmoCapacity;

        BattleAttackOpportunity opportunity = fixture.Project().Ranged;

        Assert.True(opportunity.IsReachable);
        Assert.Equal(BattleAttackPreparation.Reload, opportunity.Preparation);
        Assert.Equal(1, opportunity.ElapsedTurns);
        Assert.Equal("preparation_completed", opportunity.Reason);
    }

    [Fact]
    public void EmptyMagazineWithoutReserveIsExplicitlyUnreachable()
    {
        ProjectionFixture fixture = CreateFixture(distance: 30, typedAmmunition: true);
        fixture.Weapon.LoadedAmmo = 0;
        fixture.Weapon.ReserveAmmo = 0;

        BattleAttackOpportunity opportunity = fixture.Project().Ranged;

        Assert.False(opportunity.IsReachable);
        Assert.Equal(BattleAttackMode.Unreachable, opportunity.Mode);
        Assert.Equal("no_attainable_ranged_attack", opportunity.Reason);
        Assert.True(float.IsPositiveInfinity(opportunity.ElapsedTurns));
    }

    [Fact]
    public void LoadedWeaponThatIsNotReadiedRequiresAReadyPhase()
    {
        ProjectionFixture fixture = CreateFixture(distance: 30, ready: false);

        BattleAttackOpportunity opportunity = fixture.Project().Ranged;

        Assert.True(opportunity.IsReachable);
        Assert.Equal(BattleAttackPreparation.Ready, opportunity.Preparation);
        Assert.Equal(1, opportunity.ElapsedTurns);
        Assert.Equal("preparation_completed", opportunity.Reason);
    }

    [Fact]
    public void AimingCanBeTheEarliestRangedPreparationWindow()
    {
        ProjectionFixture fixture = CreateFixture(
            distance: 55,
            accuracy: 6,
            damage: 20,
            maximumRange: 60,
            shooterDexterity: 14,
            rangedSkill: 0);

        BattleAttackOpportunity opportunity = fixture.Project().Ranged;

        Assert.True(opportunity.IsReachable);
        Assert.Equal(BattleAttackPreparation.Aim, opportunity.Preparation);
        Assert.InRange(opportunity.ElapsedTurns, 1, 4);
        Assert.Equal("aim_completed", opportunity.Reason);
    }

    [Fact]
    public void ReadyThenAimIsReportedAsAFeasiblePreparationWindow()
    {
        ProjectionFixture fixture = CreateFixture(
            distance: 55,
            pursuerSpeed: 0,
            accuracy: 6,
            damage: 20,
            maximumRange: 60,
            ready: false,
            shooterDexterity: 14,
            rangedSkill: 0);

        BattleAttackOpportunity opportunity = fixture.Project().Ranged;

        Assert.True(opportunity.IsReachable);
        Assert.Equal(BattleAttackPreparation.Ready | BattleAttackPreparation.Aim,
            opportunity.Preparation);
        Assert.Equal(2, opportunity.ElapsedTurns);
        Assert.Equal("aim_completed", opportunity.Reason);
    }

    [Fact]
    public void AggregateDoesNotBorrowDistanceOrWeaponReachAcrossPairs()
    {
        ProjectionFixture nearButUnarmedByReach = CreateFixture(
            distance: 20,
            pursuerSpeed: 6,
            quarrySpeed: 6,
            maximumRange: 10,
            idBase: 97_000);
        ProjectionFixture farButActuallyArmed = CreateFixture(
            distance: 100,
            pursuerSpeed: 8,
            quarrySpeed: 0,
            maximumRange: 120,
            idBase: 97_100);

        BattleState state = new(
            new Dictionary<int, BattleSquad>
            {
                [nearButUnarmedByReach.Pursuer.Id] = nearButUnarmedByReach.Pursuer,
                [farButActuallyArmed.Pursuer.Id] = farButActuallyArmed.Pursuer
            },
            new Dictionary<int, BattleSquad>
            {
                [nearButUnarmedByReach.Quarry.Id] = nearButUnarmedByReach.Quarry,
                [farButActuallyArmed.Quarry.Id] = farButActuallyArmed.Quarry
            });
        BattleSquad nearPursuer = state.GetSquad(nearButUnarmedByReach.Pursuer.Id);
        BattleSquad nearQuarry = state.GetSquad(nearButUnarmedByReach.Quarry.Id);
        BattleSquad farPursuer = state.GetSquad(farButActuallyArmed.Pursuer.Id);
        BattleSquad farQuarry = state.GetSquad(farButActuallyArmed.Quarry.Id);
        BattleGridManager grid = new();
        Place(grid, nearPursuer.Soldiers[0], true, 0);
        Place(grid, nearQuarry.Soldiers[0], false, 20);
        Place(grid, farPursuer.Soldiers[0], true, 0, 100);
        Place(grid, farQuarry.Soldiers[0], false, 100, 100);
        SquadPlanningServices services = new(
            grid,
            state.Soldiers,
            new Dictionary<int, MeleeWeaponTemplate>(),
            log: null,
            new BattlePlanningContext());
        BattleAttackOpportunityProjection projection =
            new(new RangedTargetSelector(new RangedTargetingServices(services)));
        BattleAttackOpportunityProjection.PairInput nearInput = new(
            nearPursuer,
            nearQuarry,
            new BattleInterceptionProjection.Point(1, 0),
            nearButUnarmedByReach.PursuerSpeed,
            nearButUnarmedByReach.QuarrySpeed);
        BattleAttackOpportunityProjection.PairInput farInput = new(
            farPursuer,
            farQuarry,
            new BattleInterceptionProjection.Point(1, 0),
            farButActuallyArmed.PursuerSpeed,
            farButActuallyArmed.QuarrySpeed);
        Assert.Equal(BattleAttackMode.Unreachable, projection.ProjectPair(nearInput).Ranged.Mode);
        BattleAttackOpportunityProjection.AggregateResult result = projection.EarliestPair(
            [nearInput, farInput]);

        Assert.Equal(BattleAttackMode.Ranged, result.Ranged.Mode);
        Assert.Equal(farPursuer.Id, result.Ranged.PursuerSquadId);
        Assert.Equal(farQuarry.Id, result.Ranged.QuarrySquadId);
    }

    [Fact]
    public void AggregateStopsRangedSearchAfterTheFirstPairThatCanShootNow()
    {
        ProjectionFixture first = CreateFixture(distance: 30, idBase: 97_200);
        ProjectionFixture second = CreateFixture(distance: 30, idBase: 97_300);
        BattleState state = new(
            new Dictionary<int, BattleSquad>
            {
                [first.Pursuer.Id] = first.Pursuer,
                [second.Pursuer.Id] = second.Pursuer
            },
            new Dictionary<int, BattleSquad>
            {
                [first.Quarry.Id] = first.Quarry,
                [second.Quarry.Id] = second.Quarry
            });
        BattleGridManager grid = new();
        Place(grid, state.GetSquad(first.Pursuer.Id).Soldiers[0], true, 0);
        Place(grid, state.GetSquad(first.Quarry.Id).Soldiers[0], false, 30);
        Place(grid, state.GetSquad(second.Pursuer.Id).Soldiers[0], true, 0, 50);
        Place(grid, state.GetSquad(second.Quarry.Id).Soldiers[0], false, 30, 50);
        SquadPlanningServices services = new(
            grid,
            state.Soldiers,
            new Dictionary<int, MeleeWeaponTemplate>(),
            log: null,
            new BattlePlanningContext());
        BattleAttackOpportunityProjection projection =
            new(new RangedTargetSelector(new RangedTargetingServices(services)));
        BattleAttackOpportunityProjection.PairInput[] inputs =
        [
            new(state.GetSquad(second.Pursuer.Id), state.GetSquad(second.Quarry.Id),
                new BattleInterceptionProjection.Point(1, 0), 8, 0),
            new(state.GetSquad(first.Pursuer.Id), state.GetSquad(first.Quarry.Id),
                new BattleInterceptionProjection.Point(1, 0), 8, 0)
        ];

        BattleAttackOpportunityProjection.AggregateResult result = projection.EarliestPair(inputs);
        BattleAttackOpportunityProjection.PairResult later = projection.ProjectPair(
            inputs[0],
            BattleAttackOpportunityProjection.RangedSearch.None);

        // The lower-id pair wins the tie exactly as it did before the search stopped early.
        int winner = System.Math.Min(first.Pursuer.Id, second.Pursuer.Id);
        Assert.Equal(winner, result.Ranged.PursuerSquadId);
        Assert.Equal("shot_available_now", result.Ranged.Reason);
        Assert.True(BattleAttackOpportunityProjection.IsImmediate(result.Earliest));
        Assert.Equal(2, result.PairCount);
        Assert.Equal("preceded_by_earlier_pair", later.Ranged.Reason);
        Assert.False(later.Ranged.IsReachable);
    }

    [Fact]
    public void ImmediateOnlySearchFindsAShotNowAndNothingThatNeedsPreparation()
    {
        ProjectionFixture loaded = CreateFixture(distance: 30, idBase: 97_400);
        ProjectionFixture reloading = CreateFixture(
            distance: 30,
            typedAmmunition: true,
            idBase: 97_500);
        reloading.Weapon.LoadedAmmo = 0;
        reloading.Weapon.ReserveAmmo = reloading.Weapon.Template.AmmoCapacity;

        BattleAttackOpportunity loadedNow = loaded.Projection.ProjectPair(
            loaded.Input(),
            BattleAttackOpportunityProjection.RangedSearch.ImmediateOnly).Ranged;
        BattleAttackOpportunity reloadingNow = reloading.Projection.ProjectPair(
            reloading.Input(),
            BattleAttackOpportunityProjection.RangedSearch.ImmediateOnly).Ranged;
        BattleAttackOpportunity reloadingFull = reloading.Projection.ProjectPair(
            reloading.Input(),
            BattleAttackOpportunityProjection.RangedSearch.Full).Ranged;

        Assert.Equal("shot_available_now", loadedNow.Reason);
        Assert.Equal(loaded.Project().Ranged.DestinationRange, loadedNow.DestinationRange);
        Assert.False(reloadingNow.IsReachable);
        // The full search still finds the reload route the cheap search deliberately skipped.
        Assert.True(reloadingFull.IsReachable);
        Assert.Equal(BattleAttackPreparation.Reload, reloadingFull.Preparation);
    }

    [Fact]
    public void EqualSpeedPairCannotReachAClosedRangedDestinationButCanShootIfAlreadyThere()
    {
        ProjectionFixture distant = CreateFixture(
            distance: 150,
            pursuerSpeed: 6,
            quarrySpeed: 6,
            maximumRange: 30,
            accuracy: 25,
            damage: 40);

        BattleAttackOpportunity unreachable = distant.Project().Ranged;

        Assert.False(unreachable.IsReachable);
        Assert.Equal(BattleAttackMode.Unreachable, unreachable.Mode);

        ProjectionFixture inRange = CreateFixture(
            distance: 20,
            pursuerSpeed: 6,
            quarrySpeed: 6,
            maximumRange: 30,
            accuracy: 25,
            damage: 40);

        BattleAttackOpportunity current = inRange.Project().Ranged;

        Assert.True(current.IsReachable);
        Assert.Equal(0, current.ElapsedTurns);
        Assert.Equal(BattleAttackPreparation.None, current.Preparation);
    }

    [Fact]
    public void MeleeOnlyPairRetainsContactAsItsAttackOpportunity()
    {
        ProjectionFixture fixture = CreateFixture(distance: 8, ranged: false,
            pursuerSpeed: 8, quarrySpeed: 0);

        BattleAttackOpportunityProjection.PairResult result = fixture.Project();

        Assert.False(result.Ranged.IsReachable);
        Assert.Equal(BattleAttackMode.Unreachable, result.Ranged.Mode);
        Assert.True(result.Melee.IsReachable);
        Assert.Equal(BattleAttackMode.Melee, result.Melee.Mode);
        Assert.Equal(1, result.Melee.ElapsedTurns);
        Assert.Equal(BattleAttackMode.Melee, result.Earliest.Mode);
    }

    [Fact]
    public void UnpursuedQuarryEscapesWhenItsConcreteAttackOpportunityIsUnreachable()
    {
        BattleAttackOpportunity unreachable = new(
            PursuerSquadId: 84,
            QuarrySquadId: 42,
            Mode: BattleAttackMode.Unreachable,
            Preparation: BattleAttackPreparation.None,
            RequiresMovement: false,
            ElapsedTurns: float.PositiveInfinity,
            MovementTurns: float.PositiveInfinity,
            ShooterId: null,
            TargetId: null,
            WeaponTemplateId: null,
            Reason: "no_attainable_ranged_attack");

        BattleEscapeRules.Result result = BattleEscapeRules.Evaluate(new(
            Turn: 7,
            IsFirstSide: true,
            WithdrawingSquadId: 42,
            IsPursued: false,
            Threats:
            [
                new BattleEscapeRules.Threat(
                    PursuerSquadId: 84,
                    Separation: 150,
                    UsefulAttackRange: 30,
                    PursuerMoveSpeed: 6,
                    WithdrawalMoveSpeed: 6,
                    AttackOpportunity: unreachable)
            ]));

        Assert.True(result.Escapes);
        Assert.True(float.IsPositiveInfinity(result.EarliestInterceptTurns));
        Assert.Equal("no_possible_intercept", result.Reason);
        Assert.Equal(84, result.EarliestPursuerSquadId);
        Assert.Equal(42, result.EarliestQuarrySquadId);
        Assert.Equal(BattleAttackMode.Unreachable, result.EarliestAttackMode);
        Assert.Contains("attack_mode=Unreachable", result.Trace.Render());
        Assert.Contains("estimate_kind=hypothetical_useful_attack_opportunity", result.Trace.Render());
        Assert.Contains("attack_opportunity_turns=never", result.Trace.Render());
        Assert.Contains("selected_destination_condition=useful_attack", result.Trace.Render());
        Assert.Contains("selected_unreachable_reason=no_attainable_ranged_attack",
            result.Trace.Render());
    }

    private static ProjectionFixture CreateFixture(
        int distance,
        float pursuerSpeed = 8,
        float quarrySpeed = 0,
        bool ready = true,
        bool ranged = true,
        bool typedAmmunition = false,
        float accuracy = 25,
        float damage = 40,
        float maximumRange = 2000,
        float shooterDexterity = 18,
        float rangedSkill = 12,
        int idBase = 96_000)
    {
        SoldierTemplate pursuerTemplate = new(
            idBase + 1,
            TestModelFactory.HumanSpecies,
            "Projection Pursuer",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 20);
        SoldierTemplate quarryTemplate = new(
            idBase + 2,
            TestModelFactory.HumanSpecies,
            "Projection Quarry",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 20);
        Soldier pursuerModel = TestModelFactory.CreateSoldier(
            pursuerTemplate,
            "Projection Pursuer",
            dexterity: shooterDexterity,
            skills: new Skill(TestSkills.Ranged, rangedSkill));
        Soldier quarryModel = TestModelFactory.CreateSoldier(
            quarryTemplate,
            "Projection Quarry");
        pursuerModel.Id = idBase + 101;
        quarryModel.Id = idBase + 102;
        ((Soldier)pursuerModel).MoveSpeed = pursuerSpeed;
        ((Soldier)quarryModel).MoveSpeed = quarrySpeed;

        BattleSquad pursuer = new(
            false,
            TestModelFactory.CreateSquad("Projection Pursuer", pursuerModel));
        BattleSquad quarry = new(
            false,
            TestModelFactory.CreateSquad("Projection Quarry", quarryModel));
        BattleSoldier shooter = pursuer.Soldiers[0];
        BattleSoldier target = quarry.Soldiers[0];
        RangedWeapon weapon = null;
        shooter.RangedWeapons.Clear();
        shooter.ClearReadiedRangedWeapons();
        if (ranged)
        {
            AmmunitionType ammunitionType = typedAmmunition
                ? new AmmunitionType(idBase + 301, "Projection ammunition")
                : null;
            weapon = new RangedWeapon(new RangedWeaponTemplate(
                idBase + 201,
                "Projection rifle",
                EquipLocation.TwoHand,
                TestSkills.Ranged,
                accuracy,
                armorMultiplier: 1,
                penetrationMultiplier: 1,
                requiredStrength: 0,
                baseDamage: damage,
                maxDistance: maximumRange,
                rof: 1,
                ammo: 10,
                recoil: 0,
                bulk: 1,
                doesDamageDegradeWithRange: false,
                reloadTime: 1,
                ammunitionType: ammunitionType));
            shooter.RangedWeapons.Add(weapon);
            if (ready) shooter.ReadyWeapon(weapon);
        }

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0);
        Place(grid, target, false, distance);
        BattleState state = new(
            new Dictionary<int, BattleSquad> { [pursuer.Id] = pursuer },
            new Dictionary<int, BattleSquad> { [quarry.Id] = quarry });
        SquadPlanningServices services = new(
            grid,
            state.Soldiers,
            new Dictionary<int, MeleeWeaponTemplate>(),
            log: null,
            new BattlePlanningContext());
        RangedTargetSelector selector = new(new RangedTargetingServices(services));
        return new ProjectionFixture(
            state,
            grid,
            pursuer,
            quarry,
            weapon,
            new BattleAttackOpportunityProjection(selector),
            pursuerSpeed,
            quarrySpeed);
    }

    private static void Place(
        BattleGridManager grid,
        BattleSoldier soldier,
        bool firstSide,
        int x,
        int y = 0)
    {
        soldier.TopLeft = (x, y);
        grid.PlaceSoldier(soldier, firstSide, [soldier.TopLeft.Value]);
    }

    private sealed record ProjectionFixture(
        BattleState State,
        BattleGridManager Grid,
        BattleSquad Pursuer,
        BattleSquad Quarry,
        RangedWeapon Weapon,
        BattleAttackOpportunityProjection Projection,
        float PursuerSpeed,
        float QuarrySpeed)
    {
        internal BattleAttackOpportunityProjection.PairResult Project() => Projection.ProjectPair(
            new BattleAttackOpportunityProjection.PairInput(
                Pursuer,
                Quarry,
                new BattleInterceptionProjection.Point(1, 0),
                PursuerSpeed,
                QuarrySpeed));

        internal BattleAttackOpportunityProjection.PairInput Input() =>
            new(
                Pursuer,
                Quarry,
                new BattleInterceptionProjection.Point(1, 0),
                PursuerSpeed,
                QuarrySpeed);
    }
}
