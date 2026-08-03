using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class SquadEngagementPlanningTests
{
    [Fact]
    public void PairedFrame_AssignsMeleeScreenToFireSupportAgainstContactThreat()
    {
        BattleSquad assault = Squad("Assault", 81_001, 10, 0.9f);
        BattleSquad devastator = Squad("Devastator", 81_002, 20, 0.05f);
        BattleSquad lictor = Squad("Lictor", 81_003, 12, 0.95f);
        EquipMelee(assault.Soldiers[0], 91_001);
        EquipRifle(devastator.Soldiers[0], 91_002, range: 1_000, damage: 20);
        EquipMelee(lictor.Soldiers[0], 91_003);
        BattleGridManager grid = new();
        Place(grid, assault, true, 0, 0);
        Place(grid, devastator, true, 0, 10);
        Place(grid, lictor, false, 20, 10);

        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([assault, devastator], [lictor]);

        SquadEngagementFrame frame = paired.Frames[assault.Id];
        Assert.Equal(lictor.Id, frame.ScreenThreatSquadId);
        Assert.Equal(devastator.Id, frame.ProtectedSquadId);
        Assert.NotNull(frame.InterposePoint);
        Assert.Null(paired.Frames[devastator.Id].ScreenThreatSquadId);
    }

    [Fact]
    public void CapabilityProfile_ReactsToMaterialAmmoReadinessChange()
    {
        BattleSquad squad = Squad("Heavy", 81_011, 15, 0.05f);
        RangedWeapon heavy = EquipRifle(
            squad.Soldiers[0], 91_011, range: 1_200, damage: 30);
        BattleGridManager grid = new();
        Place(grid, squad, true, 0, 0);

        BattleSquadCapabilityProfile ready =
            BattleEngagementFrameBuilder.BuildProfile(squad);
        heavy.LoadedAmmo = 0;
        BattleSquadCapabilityProfile empty =
            BattleEngagementFrameBuilder.BuildProfile(squad);

        Assert.True(ready.UsableRangedBattleValue > 0);
        Assert.Equal(0, empty.UsableRangedBattleValue);
        Assert.True(ready.IsFireSupport);
        Assert.False(empty.IsFireSupport);
    }

    [Fact]
    public void CapabilityProfile_PreservesAuthoredZeroMeleeFraction()
    {
        BattleSquad squad = Squad("Pure Ranged", 81_015, 15, 0f);
        EquipRifle(squad.Soldiers[0], 91_015, range: 1_200, damage: 30);
        BattleGridManager grid = new();
        Place(grid, squad, true, 0, 0);

        BattleSquadCapabilityProfile profile =
            BattleEngagementFrameBuilder.BuildProfile(squad);

        Assert.Equal(0, profile.EffectiveMeleeFraction);
        Assert.True(profile.IsFireSupport);
        Assert.False(profile.IsContactSeeking);
    }

    [Fact]
    public void Pursuit_RangedSquadBeyondLookaheadRunsToRestoreFiringRange()
    {
        BattleSquad pursuer = Squad("Pursuer", 81_016, 20, 0.1f);
        BattleSquad quarry = Squad("Quarry", 81_017, 10, 0.1f);
        EquipRifle(pursuer.Soldiers[0], 91_016, range: 100, damage: 10);
        EquipRifle(quarry.Soldiers[0], 91_017, range: 100, damage: 10);
        ((Soldier)pursuer.Soldiers[0].Soldier).MoveSpeed = 8;
        ((Soldier)quarry.Soldiers[0].Soldier).MoveSpeed = 4;
        pursuer.LastEngagementOptionKind = EngagementOptionKind.Hold;
        BattleGridManager grid = new();
        Place(grid, pursuer, true, 0, 0);
        Place(grid, quarry, false, 2_000, 0);
        Dictionary<int, EngagementRoleConstraint> constraints = new()
        {
            [pursuer.Id] = new EngagementRoleConstraint(
                EngagementSquadRole.Pursuit,
                QuarryRunSpeed: quarry.GetSquadMove(),
                RoleTargets: [quarry])
        };
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([pursuer], [quarry], constraints);
        BattleSquadPlanner planner = Planner(grid, pursuer, quarry);

        SquadEngagementDecision decision = planner.ChooseEngagementOption(
            pursuer,
            paired.Frames[pursuer.Id],
            paired.Profiles,
            paired.Frames,
            [pursuer],
            [quarry],
            [quarry]);

        Assert.Equal(EngagementOptionKind.RunToward, decision.Chosen.Kind);
        EngagementOptionEvaluation run = decision.Candidates.Single(candidate =>
            candidate.Kind == EngagementOptionKind.RunToward);
        EngagementOptionEvaluation hold = decision.Candidates.Single(candidate =>
            candidate.Kind == EngagementOptionKind.Hold);
        Assert.True(run.RoleTerm > hold.RoleTerm);
    }

    [Fact]
    public void CoverRole_MasksOptionsAndUsesFixedWithdrawalHeading()
    {
        BattleSquad cover = Squad("Cover", 81_021, 8, 0.1f);
        BattleSquad enemy = Squad("Enemy", 81_022, 8, 0.1f);
        EquipRifle(cover.Soldiers[0], 91_021, 100, 10);
        EquipRifle(enemy.Soldiers[0], 91_022, 100, 10);
        BattleGridManager grid = new();
        Place(grid, cover, true, 0, 0);
        Place(grid, enemy, false, 10, 0);
        Dictionary<int, EngagementRoleConstraint> constraints = new()
        {
            [cover.Id] = new EngagementRoleConstraint(
                EngagementSquadRole.Cover,
                FixedHeading: 2)
        };
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([cover], [enemy], constraints);
        BattleSquadPlanner planner = Planner(grid, cover, enemy);

        SquadEngagementDecision decision = planner.ChooseEngagementOption(
            cover,
            paired.Frames[cover.Id],
            paired.Profiles,
            paired.Frames,
            [cover],
            [enemy]);

        Assert.Equal(
            [EngagementOptionKind.Hold, EngagementOptionKind.StepBack],
            decision.Candidates.Select(candidate => candidate.Kind).Order().ToArray());
        Assert.Equal((ushort)2, decision.Frame.FixedHeading);
        Assert.All(cover.AbleSoldiers, soldier => Assert.Equal(0, soldier.CurrentSpeed));
    }

    [Fact]
    public void OptionTable_HasBoundedPolicyContinuationAndNonUtilityHysteresis()
    {
        BattleSquad squad = Squad("Rifles", 81_031, 10, 0.1f);
        BattleSquad enemy = Squad("Enemy", 81_032, 10, 0.1f);
        EquipRifle(squad.Soldiers[0], 91_031, 100, 10);
        EquipRifle(enemy.Soldiers[0], 91_032, 100, 10);
        BattleGridManager grid = new();
        Place(grid, squad, true, 0, 0);
        Place(grid, enemy, false, 50, 0);
        squad.LastEngagementOptionKind = EngagementOptionKind.Hold;
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([squad], [enemy]);
        BattleSquadPlanner planner = Planner(grid, squad, enemy);

        SquadEngagementDecision decision = planner.ChooseEngagementOption(
            squad,
            paired.Frames[squad.Id],
            paired.Profiles,
            paired.Frames,
            [squad],
            [enemy]);

        Assert.All(decision.Candidates, candidate =>
            Assert.Single(candidate.FutureExchange));
        Assert.All(decision.Candidates, candidate => Assert.Equal(0, candidate.Hysteresis));
    }

    [Fact]
    public void ImmediateSquadOutput_IsCappedAtTargetsRemainingBattleValue()
    {
        BattleSquad shooters = Squad("Shooters", [(81_041, 10), (81_042, 10)], 0.05f);
        BattleSquad target = Squad("Target", 81_043, 3, 0.1f);
        foreach (BattleSoldier shooter in shooters.Soldiers)
        {
            EquipRifle(shooter, 91_040 + shooter.Soldier.Id, 100, 1_000);
        }
        EquipRifle(target.Soldiers[0], 91_043, 100, 1);
        BattleGridManager grid = new();
        Place(grid, shooters.Soldiers[0], true, 0, 0);
        Place(grid, shooters.Soldiers[1], true, 0, 2);
        Place(grid, target, false, 5, 0);
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([shooters], [target]);
        BattleSquadPlanner planner = Planner(grid, shooters, target);

        SquadEngagementDecision decision = planner.ChooseEngagementOption(
            shooters,
            paired.Frames[shooters.Id],
            paired.Profiles,
            paired.Frames,
            [shooters],
            [target]);

        Assert.All(decision.Candidates, candidate =>
            Assert.InRange(candidate.ImmediateEnemyRemoval, 0, 3));
    }

    [Fact]
    public void JogCandidate_WithNoLegalShotReceivesNoPhantomFireValue()
    {
        BattleSquad squad = Squad("Short Range", 81_051, 10, 0.1f);
        BattleSquad enemy = Squad("Distant", 81_052, 10, 0.1f);
        EquipRifle(squad.Soldiers[0], 91_051, 3, 10);
        EquipRifle(enemy.Soldiers[0], 91_052, 3, 10);
        BattleGridManager grid = new();
        Place(grid, squad, true, 0, 0);
        Place(grid, enemy, false, 5, 0);
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([squad], [enemy]);
        BattleSquadPlanner planner = Planner(grid, squad, enemy);

        SquadEngagementDecision decision = planner.ChooseEngagementOption(
            squad, paired.Frames[squad.Id], paired.Profiles, paired.Frames,
            [squad], [enemy]);
        EngagementOptionEvaluation jog = decision.Candidates.Single(candidate =>
            candidate.Kind == EngagementOptionKind.JogToward);

        Assert.Equal(0, jog.ImmediateEnemyRemoval);
        Assert.All(jog.RootActions, action =>
            Assert.NotEqual(PlannedSoldierActionKind.Shoot, action.Kind));
    }

    [Fact]
    public void JogCandidate_ExcludesIllegalAimAndPlansTheMovingShot()
    {
        BattleSquad squad = Squad("Joggers", 81_061, 10, 0.1f);
        BattleSquad enemy = Squad("Far Target", 81_062, 10, 0.1f);
        ((Soldier)squad.Soldiers[0].Soldier).Dexterity = 20;
        EquipRifle(squad.Soldiers[0], 91_061, 100, 10);
        EquipRifle(enemy.Soldiers[0], 91_062, 100, 10);
        BattleGridManager grid = new();
        Place(grid, squad, true, 0, 0);
        Place(grid, enemy, false, 5, 0);
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([squad], [enemy]);
        BattleSquadPlanner planner = Planner(grid, squad, enemy);

        SquadEngagementDecision decision = planner.ChooseEngagementOption(
            squad, paired.Frames[squad.Id], paired.Profiles, paired.Frames,
            [squad], [enemy]);
        PlannedSoldierAction action = decision.Candidates.Single(candidate =>
                candidate.Kind == EngagementOptionKind.JogToward)
            .RootActions.Single();

        Assert.Equal(PlannedSoldierActionKind.Shoot, action.Kind);
        Assert.Equal(0, action.AimMultiplier);
    }

    [Fact]
    public void RootActionDescriptor_IsTheActionMaterializedForExecution()
    {
        BattleSquad squad = Squad("Shooters", 81_071, 10, 0.1f);
        BattleSquad enemy = Squad("Target", 81_072, 10, 0.1f);
        EquipRifle(squad.Soldiers[0], 91_071, 100, 100);
        EquipRifle(enemy.Soldiers[0], 91_072, 100, 1);
        BattleGridManager grid = new();
        Place(grid, squad, true, 0, 0);
        Place(grid, enemy, false, 5, 0);
        List<IAction> shootActions = [];
        Dictionary<int, BattleSoldier> soldiers = squad.Soldiers.Concat(enemy.Soldiers)
            .ToDictionary(soldier => soldier.Soldier.Id);
        BattleSquadPlanner planner = new(
            grid, soldiers, shootActions, [], [], null,
            new Dictionary<int, MeleeWeaponTemplate>(), new SeededRNG(81_071));
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([squad], [enemy]);
        SquadEngagementDecision scored = planner.ChooseEngagementOption(
            squad, paired.Frames[squad.Id], paired.Profiles, paired.Frames,
            [squad], [enemy]);
        EngagementOptionEvaluation hold = scored.Candidates.Single(candidate =>
            candidate.Kind == EngagementOptionKind.Hold);
        SquadEngagementDecision selected = scored with { Chosen = hold };

        planner.DeclareEngagementDecision(selected);
        planner.BuildEngagementActions(selected);

        PlannedSoldierAction root = Assert.Single(hold.RootActions);
        IAction materialized = Assert.Single(shootActions);
        Assert.Equal(root.SoldierId, materialized.ActorId);
        if (root.Kind == PlannedSoldierActionKind.Shoot)
        {
            ShootAction shot = Assert.IsType<ShootAction>(materialized);
            Assert.Equal(root.TargetId, shot.TargetId);
            Assert.Equal(root.WeaponTemplateId, shot.WeaponId);
        }
        else
        {
            Assert.Equal(PlannedSoldierActionKind.Aim, root.Kind);
            Assert.IsType<AimAction>(materialized);
        }
    }

    [Fact]
    public void CandidateTargetSpeed_UsesLiveRangeModifierMath()
    {
        BattleSquad attackers = Squad("Attackers", 81_081, 10, 0.1f);
        BattleSquad targets = Squad("Targets", 81_082, 10, 0.1f);
        RangedWeapon weapon = EquipRifle(attackers.Soldiers[0], 91_081, 100, 10);
        EquipRifle(targets.Soldiers[0], 91_082, 100, 10);
        BattleGridManager grid = new();
        Place(grid, attackers, true, 0, 0);
        Place(grid, targets, false, 20, 0);
        BattleSquadPlanner planner = Planner(grid, attackers, targets);

        BattleSquadPlanner.RangedTargetEvaluation still = planner.EvaluateRangedTarget(
            attackers.Soldiers[0], targets.Soldiers[0], weapon, 20, 0, 0);
        BattleSquadPlanner.RangedTargetEvaluation moving = planner.EvaluateRangedTarget(
            attackers.Soldiers[0], targets.Soldiers[0], weapon, 20, 0, 4);

        Assert.True(moving.HitProbability < still.HitProbability);
        targets.Soldiers[0].CurrentSpeed = 4;
        BattleSquadPlanner liveStatePlanner = Planner(grid, attackers, targets);
        BattleSquadPlanner.RangedTargetEvaluation live = liveStatePlanner.EvaluateRangedTarget(
            attackers.Soldiers[0], targets.Soldiers[0], weapon, 20, 0);
        Assert.Equal(live.HitProbability, moving.HitProbability);
    }

    [Fact]
    public void RoutingSquad_FleesOnOneHeading_WhenMembersHaveDifferentNearestEnemies()
    {
        // Each man used to run from whichever enemy was nearest to him personally, so a squad
        // caught between two threats broke into fragments heading opposite ways — while pursuit,
        // the engagement frame and the escape rules all kept steering at a squad centroid that now
        // sat in empty ground between them. The rout heading belongs to the squad.
        BattleSquad routers = Squad("Routers", [(81_091, 10), (81_092, 10)], 0.1f);
        BattleSquad west = Squad("West", 81_093, 10, 0.1f);
        BattleSquad east = Squad("East", 81_094, 10, 0.1f);
        EquipRifle(routers.Soldiers[0], 91_091, 100, 10);
        EquipRifle(routers.Soldiers[1], 91_092, 100, 10);
        EquipRifle(west.Soldiers[0], 91_093, 100, 10);
        EquipRifle(east.Soldiers[0], 91_094, 100, 10);
        BattleGridManager grid = new();
        Place(grid, routers.Soldiers[0], true, 0, 0);
        Place(grid, routers.Soldiers[1], true, 0, 20);
        // West is the closest threat in the force; East is nonetheless the nearest enemy to the
        // second router, which is exactly what used to split the squad.
        Place(grid, west, false, -30, 0);
        Place(grid, east, false, 40, 20);
        List<IAction> moveActions = [];
        Dictionary<int, BattleSoldier> soldiers = routers.Soldiers
            .Concat(west.Soldiers).Concat(east.Soldiers)
            .ToDictionary(soldier => soldier.Soldier.Id);
        BattleSquadPlanner planner = new(
            grid, soldiers, [], moveActions, [], null,
            new Dictionary<int, MeleeWeaponTemplate>(), new SeededRNG(81_091));

        planner.PrepareRoutingActions(routers);

        List<MoveAction> moves = moveActions.Cast<MoveAction>().ToList();
        Assert.Equal(2, moves.Count);
        Assert.All(moves, move => Assert.True(
            move.Destination.Item1 > move.Origin.Item1,
            $"expected flight away from the squad's closest threat (+x), got {move.Description()}"));
        // Cohesion: a shared heading at a shared tier must not stretch the squad out.
        float before = Distance(
            routers.Soldiers[0].TopLeft.Value, routers.Soldiers[1].TopLeft.Value);
        float after = Distance(moves[0].Destination, moves[1].Destination);
        Assert.InRange(after, 0, before + 1.0001f);
    }

    private static float Distance(ValueTuple<int, int> first, ValueTuple<int, int> second)
    {
        int dx = first.Item1 - second.Item1;
        int dy = first.Item2 - second.Item2;
        return (float)System.Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static BattleSquad Squad(
        string name,
        int soldierId,
        int battleValue,
        float meleeFraction)
    {
        return Squad(name, [(soldierId, battleValue)], meleeFraction);
    }

    private static BattleSquad Squad(
        string name,
        IReadOnlyCollection<(int Id, int BattleValue)> members,
        float meleeFraction)
    {
        List<Soldier> soldiers = members.Select(member =>
        {
            SoldierTemplate template = new(
                100_000 + member.Id,
                TestModelFactory.HumanSpecies,
                $"{name} Template",
                1,
                1,
                false,
                0,
                [],
                battleValue: member.BattleValue,
                meleeFraction: meleeFraction);
            Soldier soldier = TestModelFactory.CreateSoldier(template, name);
            soldier.Id = member.Id;
            return soldier;
        }).ToList();
        return new BattleSquad(false, TestModelFactory.CreateSquad(name, soldiers.ToArray()));
    }

    private static RangedWeapon EquipRifle(
        BattleSoldier soldier,
        int id,
        float range,
        float damage)
    {
        RangedWeapon weapon = new(new RangedWeaponTemplate(
            id,
            "Planning Rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 6,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: damage,
            maxDistance: range,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 2,
            doesDamageDegradeWithRange: false,
            reloadTime: 1));
        soldier.RangedWeapons.Clear();
        soldier.ClearReadiedRangedWeapons();
        soldier.RangedWeapons.Add(weapon);
        soldier.ReadyWeapon(weapon);
        return weapon;
    }

    private static void EquipMelee(BattleSoldier soldier, int id)
    {
        MeleeWeapon weapon = new(new MeleeWeaponTemplate(
            id,
            "Planning Blade",
            EquipLocation.OneHand,
            TestSkills.Melee,
            accuracy: 4,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            strengthMultiplier: 4,
            parryMod: 2,
            attackSpeedMultiplier: 2));
        soldier.RangedWeapons.Clear();
        soldier.ClearReadiedRangedWeapons();
        soldier.MeleeWeapons.Clear();
        soldier.ClearReadiedMeleeWeapons();
        soldier.MeleeWeapons.Add(weapon);
        soldier.ReadyWeapon(weapon);
    }

    private static void Place(
        BattleGridManager grid,
        BattleSquad squad,
        bool side,
        int x,
        int y) => Place(grid, squad.Soldiers[0], side, x, y);

    private static void Place(
        BattleGridManager grid,
        BattleSoldier soldier,
        bool side,
        int x,
        int y)
    {
        soldier.TopLeft = (x, y);
        grid.PlaceSoldier(soldier, side, [(x, y)]);
    }

    private static BattleSquadPlanner Planner(
        BattleGridManager grid,
        params BattleSquad[] squads)
    {
        Dictionary<int, BattleSoldier> soldiers = squads
            .SelectMany(squad => squad.Soldiers)
            .ToDictionary(soldier => soldier.Soldier.Id);
        Dictionary<int, MeleeWeaponTemplate> melee = soldiers.Values
            .SelectMany(soldier => soldier.MeleeWeapons
                .Select(weapon => weapon.Template)
                .Append(soldier.Soldier.Template.Species.DefaultUnarmedWeapon))
            .GroupBy(template => template.Id)
            .ToDictionary(group => group.Key, group => group.First());
        return new BattleSquadPlanner(
            grid,
            soldiers,
            new List<IAction>(),
            new List<IAction>(),
            new List<IAction>(),
            null,
            melee,
            new SeededRNG(81_000));
    }
}
