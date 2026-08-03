using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattlePursuitActionPlannerTests
{
    [Fact]
    public void JogToward_MovesTowardQuarryAndMaterializesItsPlannedShot()
    {
        BattleSquad pursuer = CreateSquad("Pursuer", 72_001);
        BattleSquad withdrawing = CreateSquad("Withdrawer", 72_002);
        Fixture fixture = CreateFixture(
            (pursuer, true, 0, 0),
            (withdrawing, false, 10, 0));

        PlanPursuit(fixture, pursuer, [withdrawing], EngagementOptionKind.JogToward);

        Assert.Equal(SquadMovementTier.Jog, pursuer.MovementTier);
        MoveAction move = Assert.IsType<MoveAction>(Assert.Single(fixture.MoveActions));
        Assert.Matches(@"to \([1-9]\d*, -?\d+\)", move.Description());
        ShootAction shot = Assert.IsType<ShootAction>(Assert.Single(fixture.ShootActions));
        Assert.Equal(withdrawing.Soldiers[0].Soldier.Id, shot.TargetId);
    }

    [Fact]
    public void PursuitChoosesRunToward_WhenJogHasNoWorthwhileShot()
    {
        // The withdrawer is far beyond the test rifle's 100-yd maximum range: a jog-and-fire
        // follow would just fall further behind, so the squad sprints to regain effective
        // range instead of shooting at nothing.
        BattleSquad pursuer = CreateSquad("Pursuer", 72_031);
        BattleSquad withdrawing = CreateSquad("Withdrawer", 72_032);
        Fixture fixture = CreateFixture(
            (pursuer, true, 0, 0),
            (withdrawing, false, 400, 0));

        SquadEngagementDecision decision = PlanPursuit(fixture, pursuer, [withdrawing]);

        Assert.Equal(EngagementOptionKind.RunToward, decision.Chosen.Kind);
        Assert.Equal(SquadMovementTier.Run, pursuer.MovementTier);
        Assert.Empty(fixture.ShootActions);
        MoveAction move = Assert.IsType<MoveAction>(Assert.Single(fixture.MoveActions));
        Assert.Matches(@"to \([1-9]\d*, -?\d+\)", move.Description());
    }

    [Fact]
    public void Hold_BringsTheGunsToBearWithoutMoving()
    {
        // The chase is unwinnable, so the squad plants itself and works the target with
        // stationary fire — no Bulk penalty, and the aim bonus is allowed to accumulate —
        // rather than jogging after a quarry it cannot catch.
        BattleSquad pursuer = CreateSquad("Pursuer", 72_041);
        BattleSquad withdrawing = CreateSquad("Withdrawer", 72_042);
        Fixture fixture = CreateFixture(
            (pursuer, true, 0, 0),
            (withdrawing, false, 10, 0));

        PlanPursuit(fixture, pursuer, [withdrawing], EngagementOptionKind.Hold);

        Assert.Equal(SquadMovementTier.Stationary, pursuer.MovementTier);
        Assert.Empty(fixture.MoveActions);
        Assert.Empty(fixture.MeleeActions);
        // Aiming and firing both land in the shoot segment; which one the planner picks this
        // turn is its own business, but it must be engaging the withdrawer either way.
        Assert.NotEmpty(fixture.ShootActions);
    }

    [Fact]
    public void RunToward_MovesTowardPrimaryWithdrawerWithoutShooting()
    {
        BattleSquad pursuer = CreateSquad("Pursuer", 72_011);
        BattleSquad farther = CreateSquad("Far", 72_012);
        BattleSquad nearerRearGuard = CreateSquad("Rear Guard", 72_013);
        Fixture fixture = CreateFixture(
            (pursuer, true, 0, 0),
            (farther, false, 20, 0),
            (nearerRearGuard, false, 8, 0));

        PlanPursuit(fixture, pursuer, [farther, nearerRearGuard],
            EngagementOptionKind.RunToward);

        Assert.Equal(SquadMovementTier.Run, pursuer.MovementTier);
        Assert.Empty(fixture.ShootActions);
        MoveAction move = Assert.IsType<MoveAction>(Assert.Single(fixture.MoveActions));
        Assert.Matches(@"to \([1-9]\d*, -?\d+\)", move.Description());
    }

    [Fact]
    public void CloseToContact_AttacksTheWithdrawerItHasCaught()
    {
        // Press exists to convert contact into damage. Adjacent to the quarry it swings; without
        // this the posture closed the distance and then jogged in place beside the enemy forever,
        // so a fast element could never pin anyone for the slow element to catch up to.
        BattleSquad pursuer = CreateSquad("Pursuer", 72_051);
        BattleSquad withdrawing = CreateSquad("Withdrawer", 72_052);
        Fixture fixture = CreateFixture(
            (pursuer, true, 0, 0),
            (withdrawing, false, 1, 0));

        PlanPursuit(fixture, pursuer, [withdrawing], EngagementOptionKind.CloseToContact);

        // Which attack it makes is the engaged-soldier decision's business — a rifleman standing
        // on his quarry may well shoot point blank rather than club him. What matters is that
        // reaching the enemy produces an attack at all instead of another stride.
        SquadChargeIntentAction charge = Assert.IsType<SquadChargeIntentAction>(
            Assert.Single(fixture.MoveActions));
        charge.Execute(null);

        Assert.NotEmpty(fixture.MeleeActions.Concat(fixture.ShootActions));
        Assert.Empty(charge.ResolvedMovementActions);
    }

    [Fact]
    public void CloseToContact_ChargesWhenTheWithdrawerIsWithinOneMove()
    {
        // Just out of contact but inside a Run: the pursuer closes and gets stuck in the same
        // turn rather than stopping politely one pace short.
        BattleSquad pursuer = CreateSquad("Pursuer", 72_061);
        BattleSquad withdrawing = CreateSquad("Withdrawer", 72_062);
        Fixture fixture = CreateFixture(
            (pursuer, true, 0, 0),
            (withdrawing, false, 4, 0));

        PlanPursuit(fixture, pursuer, [withdrawing], EngagementOptionKind.CloseToContact);

        // It closes and gets stuck in on the same turn: a move plus an attack, not a bare stride
        // that stops one pace short and repeats forever.
        SquadChargeIntentAction charge = Assert.IsType<SquadChargeIntentAction>(
            Assert.Single(fixture.MoveActions));
        charge.Execute(null);

        Assert.NotEmpty(charge.ResolvedMovementActions);
        Assert.NotEmpty(fixture.MeleeActions.Concat(fixture.ShootActions));
    }

    [Fact]
    public void MovingQuarryInsideChargeRange_IsPursuedAfterItsMoveWithoutTeleporting()
    {
        // Movement resolves simultaneously. Charging the quarry's current square would use only
        // enough movement to stop beside that stale position; the Routing squad then runs away and
        // the same false charge repeats forever. A faster pursuer runs through the old position
        // until it begins a turn in real contact.
        BattleSquad pursuer = CreateSquad("Pursuer", 72_063, Loadout.Assault);
        BattleSquad withdrawing = CreateSquad("Withdrawer", 72_064);
        ((Soldier)pursuer.Soldiers[0].Soldier).MoveSpeed = 8;
        ((Soldier)withdrawing.Soldiers[0].Soldier).MoveSpeed = 4;
        Fixture fixture = CreateFixture(
            (pursuer, true, 0, 0),
            (withdrawing, false, 6, 0));

        SquadEngagementDecision decision = PlanPursuit(
            fixture,
            pursuer,
            [withdrawing],
            targetRoles: new Dictionary<int, EngagementSquadRole>
            {
                [withdrawing.Id] = EngagementSquadRole.Routing
            });

        Assert.Equal(EngagementOptionKind.CloseToContact, decision.Chosen.Kind);
        SquadChargeIntentAction charge = Assert.IsType<SquadChargeIntentAction>(
            Assert.Single(fixture.MoveActions));

        fixture.Grid.MoveSoldier(withdrawing.Soldiers[0], (10, 0), 0);
        withdrawing.Soldiers[0].TopLeft = (10, 0);
        charge.Execute(null);

        MoveAction pursuitMove = Assert.IsType<MoveAction>(
            Assert.Single(charge.ResolvedMovementActions));
        float remaining = fixture.Grid.GetDistanceBetweenSoldiers(
            pursuer.Soldiers[0].Soldier.Id,
            withdrawing.Soldiers[0].Soldier.Id);
        Assert.InRange(remaining, 1.0001f, 5.9999f);
        Assert.True(GridDistance((0, 0), pursuitMove.Destination) <= 8.0001f);
        Assert.Empty(fixture.MeleeActions);
    }

    [Fact]
    public void BreakOff_HoldsAndCreatesNoCombatActions()
    {
        BattleSquad pursuer = CreateSquad("Pursuer", 72_021);
        BattleSquad withdrawing = CreateSquad("Withdrawer", 72_022);
        Fixture fixture = CreateFixture(
            (pursuer, true, 0, 0),
            (withdrawing, false, 8, 0));

        SquadEngagementDecision decision = PlanPursuit(
            fixture,
            pursuer,
            [withdrawing],
            role: EngagementSquadRole.BreakOff);

        Assert.Equal(EngagementOptionKind.Hold, decision.Chosen.Kind);
        Assert.Equal(SquadMovementTier.Stationary, pursuer.MovementTier);
        Assert.Empty(fixture.MoveActions);
        Assert.Empty(fixture.ShootActions);
        Assert.Empty(fixture.MeleeActions);
    }

    [Fact]
    public void FireSupportChoosesHold_WhileAssaultChoosesFastAdvance()
    {
        // The posture a pursuer should take is a property of what it is carrying, not of the force
        // it belongs to. A sniper squad already inside its effective range is at its best standing
        // still; a pistol-and-chainsword squad is worth nothing until it arrives. Both are chasing
        // the same quarry at the same distance here, so only the loadout differs.
        BattleSquad snipers = CreateSquad("Scouts", 72_071, Loadout.FireSupport);
        BattleSquad assault = CreateSquad("Assault", 72_072, Loadout.Assault);
        BattleSquad withdrawing = CreateSquad("Withdrawer", 72_073, Loadout.Rifle);
        Fixture fixture = CreateFixture(
            (snipers, true, 0, 0),
            (assault, true, 0, 20),
            (withdrawing, false, 60, 0));

        IReadOnlyCollection<BattleSquad> friendly = [snipers, assault];
        SquadEngagementDecision sniperDecision = PlanPursuit(
            fixture, snipers, [withdrawing], friendlySquads: friendly,
            materialize: false);
        SquadEngagementDecision assaultDecision = PlanPursuit(
            fixture, assault, [withdrawing], friendlySquads: friendly,
            materialize: false);

        Assert.Equal(EngagementOptionKind.Hold, sniperDecision.Chosen.Kind);
        Assert.True(assaultDecision.Chosen.Kind is
            EngagementOptionKind.RunToward or EngagementOptionKind.CloseToContact);
    }

    [Fact]
    public void RunToward_UsesCurrentRoleConstraintToPreferCoverOverNearerBoundSquad()
    {
        // Bound squads cannot shoot while the Cover squad can. The pursuit frame therefore selects
        // the Cover squad as its primary even though the Bound squad is nearer (10 along x versus
        // 30 along y), and the unified RunToward candidate follows that primary.
        BattleSquad pursuer = CreateSquad("Pursuer", 72_081, Loadout.Rifle);
        BattleSquad bound = CreateSquad("Bound", 72_082, Loadout.Rifle);
        BattleSquad cover = CreateSquad("Cover", 72_083, Loadout.Rifle);
        // Deliberately stale live roles disagree with this turn's paired constraints.
        bound.WithdrawalRole = WithdrawalRole.Cover;
        cover.WithdrawalRole = WithdrawalRole.Bound;
        Fixture fixture = CreateFixture(
            (pursuer, true, 0, 0),
            (bound, false, 10, 0),
            (cover, false, 0, 30));

        SquadEngagementDecision decision = PlanPursuit(
            fixture,
            pursuer,
            [bound, cover],
            EngagementOptionKind.RunToward,
            targetRoles: new Dictionary<int, EngagementSquadRole>
            {
                [bound.Id] = EngagementSquadRole.Bound,
                [cover.Id] = EngagementSquadRole.Cover
            });

        Assert.Equal(cover.Id, decision.Frame.PrimaryCounterpartSquadId);
        MoveAction move = Assert.IsType<MoveAction>(Assert.Single(fixture.MoveActions));
        // Assert the bearing, not the exact square: destination selection sidesteps to avoid
        // reserved ground, so the pursuer may drift a pace off the true line to the primary.
        Match destination = Regex.Match(move.Description(), @"to \((-?\d+), (-?\d+)\)");
        Assert.True(destination.Success, move.Description());
        int x = int.Parse(destination.Groups[1].Value);
        int y = int.Parse(destination.Groups[2].Value);
        Assert.True(y > x, $"expected movement toward the covering primary (+y), got ({x}, {y})");
    }

    [Fact]
    public void Pursuers_ShootTheCoveringSquadRatherThanTheNearerFleeingOne()
    {
        // The bound squad is nearer and therefore the easier shot on raw expected damage — but it
        // is running, so it cannot shoot back. The covering squad is the only source of enemy fire
        // in an organized withdrawal, and it is what should be getting shot at.
        BattleSquad pursuer = CreateSquad("Pursuer", 72_091, Loadout.Rifle);
        BattleSquad bound = CreateSquad("Bound", 72_092, Loadout.Rifle);
        BattleSquad cover = CreateSquad("Cover", 72_093, Loadout.Rifle);
        bound.WithdrawalRole = WithdrawalRole.Bound;
        cover.WithdrawalRole = WithdrawalRole.Cover;
        Fixture fixture = CreateFixture(
            (pursuer, true, 0, 0),
            (bound, false, 10, 0),
            (cover, false, 14, 0));

        PlanPursuit(fixture, pursuer, [bound, cover], EngagementOptionKind.Hold);

        ShootAction shot = Assert.IsType<ShootAction>(Assert.Single(fixture.ShootActions));
        Assert.Equal(cover.Soldiers[0].Soldier.Id, shot.TargetId);
    }

    [Fact]
    public void DistantHoldingSquad_FinishesItsAimAndFires_RatherThanAimingForever()
    {
        // Regression for "the scouts sit, aim, and never fire". A Standoff fire-support squad is
        // stationary and far away by design, so its aim has to survive from turn to turn — an aim
        // discarded and restarted never reaches the bonus that triggers the shot.
        //
        // The target is deliberately Bound. Releasing a fleeing target inside
        // ShouldInterruptStickyTarget also released it inside IsExistingAimStillViable, which
        // shares that predicate, so the aim was thrown away every turn however good the shot was.
        // Handing the squad an already-matured aim makes the failure a single deterministic step:
        // honour it and fire, or discard it and start aiming over.
        BattleSquad snipers = CreateSquad("Scouts", 72_111, Loadout.FireSupport);
        BattleSquad withdrawing = CreateSquad("Withdrawer", 72_112, Loadout.Rifle);
        withdrawing.WithdrawalRole = WithdrawalRole.Bound;
        Fixture fixture = CreateFixture(
            (snipers, true, 0, 0),
            (withdrawing, false, 300, 0));
        BattleSoldier sniper = snipers.Soldiers[0];
        sniper.Aim = new ValueTuple<int, RangedWeapon, int>(
            withdrawing.Soldiers[0].Soldier.Id,
            sniper.EquippedRangedWeapons[0],
            3);

        PlanPursuit(fixture, snipers, [withdrawing], EngagementOptionKind.Hold);

        ShootAction shot = Assert.IsType<ShootAction>(Assert.Single(fixture.ShootActions));
        Assert.Equal(withdrawing.Soldiers[0].Soldier.Id, shot.TargetId);
        Assert.NotNull(sniper.Aim);
    }

    [Fact]
    public void FleeingTargetBias_IsInertWhenNobodyIsWithdrawing()
    {
        // Same geometry, no withdrawal roles set: the ordinary expected-damage scorer is untouched
        // and still takes the nearer, easier target. This is what keeps the bias out of every
        // battle that is not a pursuit.
        BattleSquad pursuer = CreateSquad("Pursuer", 72_101, Loadout.Rifle);
        BattleSquad nearer = CreateSquad("Nearer", 72_102, Loadout.Rifle);
        BattleSquad farther = CreateSquad("Farther", 72_103, Loadout.Rifle);
        Fixture fixture = CreateFixture(
            (pursuer, true, 0, 0),
            (nearer, false, 10, 0),
            (farther, false, 14, 0));

        PlanPursuit(fixture, pursuer, [nearer, farther], EngagementOptionKind.Hold);

        ShootAction shot = Assert.IsType<ShootAction>(Assert.Single(fixture.ShootActions));
        Assert.Equal(nearer.Soldiers[0].Soldier.Id, shot.TargetId);
    }

    private static SquadEngagementDecision PlanPursuit(
        Fixture fixture,
        BattleSquad pursuer,
        IReadOnlyCollection<BattleSquad> withdrawing,
        EngagementOptionKind? selectedKind = null,
        EngagementSquadRole role = EngagementSquadRole.Pursuit,
        IReadOnlyCollection<BattleSquad> friendlySquads = null,
        bool materialize = true,
        IReadOnlyDictionary<int, EngagementSquadRole> targetRoles = null)
    {
        IReadOnlyCollection<BattleSquad> friendly = friendlySquads ?? [pursuer];
        float quarryRunSpeed = withdrawing.Count == 0
            ? 0
            : withdrawing.Min(squad => squad.GetSquadMove());
        Dictionary<int, EngagementRoleConstraint> constraints = new()
        {
            [pursuer.Id] = new EngagementRoleConstraint(
                role,
                QuarryRunSpeed: quarryRunSpeed,
                RoleTargets: withdrawing)
        };
        foreach (BattleSquad target in withdrawing)
        {
            EngagementSquadRole targetRole = targetRoles?.GetValueOrDefault(target.Id)
                ?? (target.WithdrawalRole switch
                {
                    WithdrawalRole.Cover => EngagementSquadRole.Cover,
                    WithdrawalRole.RearGuard => EngagementSquadRole.RearGuard,
                    WithdrawalRole.Bound => EngagementSquadRole.Bound,
                    WithdrawalRole.Routing => EngagementSquadRole.Routing,
                    _ => EngagementSquadRole.Normal
                });
            constraints[target.Id] = new EngagementRoleConstraint(targetRole);
        }
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build(friendly, withdrawing, constraints);
        SquadEngagementDecision scored = fixture.Planner.ChooseEngagementOption(
            pursuer,
            paired.Frames[pursuer.Id],
            paired.Profiles,
            paired.Frames,
            friendly,
            withdrawing,
            withdrawing);
        SquadEngagementDecision selected = selectedKind.HasValue
            ? scored with
            {
                Chosen = scored.Candidates.Single(candidate => candidate.Kind == selectedKind.Value)
            }
            : scored;
        if (materialize)
        {
            fixture.Planner.DeclareEngagementDecision(selected);
            fixture.Planner.BuildEngagementActions(selected);
        }
        return selected;
    }

    private enum Loadout { Rifle, FireSupport, Assault }

    private static BattleSquad CreateSquad(
        string name,
        int soldierId,
        Loadout loadout = Loadout.Rifle)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(
            name: name,
            dexterity: 18,
            skills: [new Skill(TestSkills.Ranged, 12), new Skill(TestSkills.Melee, 12)]);
        soldier.Id = soldierId;
        BattleSquad squad = new(false, TestModelFactory.CreateSquad(name, soldier));
        if (loadout != Loadout.Rifle)
        {
            Equip(squad.Soldiers[0], loadout, soldierId);
        }

        return squad;
    }

    // Two deliberately extreme loadouts so the posture test turns on the weapons rather than on
    // where a tuning constant happens to sit this week.
    private static void Equip(BattleSoldier soldier, Loadout loadout, int seed)
    {
        soldier.RangedWeapons.Clear();
        soldier.ClearReadiedRangedWeapons();
        soldier.MeleeWeapons.Clear();
        soldier.ClearReadiedMeleeWeapons();

        if (loadout == Loadout.FireSupport)
        {
            RangedWeapon sniper = new(new RangedWeaponTemplate(
                seed, "Test Sniper Rifle", EquipLocation.TwoHand, TestSkills.Ranged,
                accuracy: 10, armorMultiplier: 1, penetrationMultiplier: 1, requiredStrength: 0,
                baseDamage: 12, maxDistance: 800, rof: 1, ammo: 10, recoil: 0, bulk: 1,
                doesDamageDegradeWithRange: false, reloadTime: 1, 0, 0, 0));
            soldier.RangedWeapons.Add(sniper);
            soldier.ReadyWeapon(sniper);
            return;
        }

        RangedWeapon pistol = new(new RangedWeaponTemplate(
            seed, "Test Pistol", EquipLocation.OneHand, TestSkills.Ranged,
            accuracy: 0, armorMultiplier: 1, penetrationMultiplier: 1, requiredStrength: 0,
            baseDamage: 1, maxDistance: 6, rof: 1, ammo: 6, recoil: 0, bulk: 1,
            doesDamageDegradeWithRange: true, reloadTime: 1, 0, 0, 0));
        MeleeWeapon chainsword = new(new MeleeWeaponTemplate(
            seed + 1, "Test Chainsword", EquipLocation.OneHand, TestSkills.Melee,
            accuracy: 3, armorMultiplier: 1, penetrationMultiplier: 1, requiredStrength: 0,
            strengthMultiplier: 4, parryMod: 2, attackSpeedMultiplier: 2));
        soldier.RangedWeapons.Add(pistol);
        soldier.ReadyWeapon(pistol);
        soldier.MeleeWeapons.Add(chainsword);
        soldier.ReadyWeapon(chainsword);
    }

    private static Fixture CreateFixture(
        params (BattleSquad Squad, bool Side, int X, int Y)[] placements)
    {
        BattleGridManager grid = new();
        foreach ((BattleSquad squad, bool side, int x, int y) in placements)
        {
            BattleSoldier soldier = squad.Soldiers[0];
            soldier.TopLeft = new ValueTuple<int, int>(x, y);
            grid.PlaceSoldier(soldier, side, [soldier.TopLeft.Value]);
        }

        Dictionary<int, BattleSoldier> soldiers = placements
            .SelectMany(placement => placement.Squad.Soldiers)
            .ToDictionary(soldier => soldier.Soldier.Id);
        List<IAction> shootActions = [];
        List<IAction> moveActions = [];
        List<IAction> meleeActions = [];
        BattleSquadPlanner planner = new(
            grid,
            soldiers,
            shootActions,
            moveActions,
            meleeActions,
            null,
            CreateMeleeTemplateMap(soldiers.Values),
            new SeededRNG(72_000));
        return new Fixture(grid, planner, shootActions, moveActions, meleeActions);
    }

    private static IReadOnlyDictionary<int, MeleeWeaponTemplate> CreateMeleeTemplateMap(
        IEnumerable<BattleSoldier> soldiers)
    {
        return soldiers
            .SelectMany(soldier => soldier.MeleeWeapons
                .Concat(soldier.EquippedMeleeWeapons)
                .Select(weapon => weapon.Template)
                .Append(soldier.Soldier.Template.Species.DefaultUnarmedWeapon))
            .GroupBy(template => template.Id)
            .ToDictionary(group => group.Key, group => group.First());
    }

    private static float GridDistance((int X, int Y) first, (int X, int Y) second)
    {
        int dx = first.X - second.X;
        int dy = first.Y - second.Y;
        return (float)System.Math.Sqrt(dx * dx + dy * dy);
    }

    private sealed record Fixture(
        BattleGridManager Grid,
        BattleSquadPlanner Planner,
        List<IAction> ShootActions,
        List<IAction> MoveActions,
        List<IAction> MeleeActions);
}
