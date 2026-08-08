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
    public void PairedFrame_ScreensAThreatAtTheLongApproachDistance()
    {
        BattleSquad assault = Squad("Long Assault", 81_004, 10, 0.9f);
        BattleSquad devastator = Squad("Long Devastator", 81_005, 20, 0.05f);
        BattleSquad lictor = Squad("Long Lictor", 81_006, 12, 0.95f);
        EquipMelee(assault.Soldiers[0], 91_004);
        EquipRifle(devastator.Soldiers[0], 91_005, 1_000, 20);
        EquipMelee(lictor.Soldiers[0], 91_006);
        BattleGridManager grid = new();
        Place(grid, assault, true, 0, 0);
        Place(grid, devastator, true, 0, 10);
        Place(grid, lictor, false, 418, 10);

        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([assault, devastator], [lictor]);

        Assert.Equal(lictor.Id, paired.Frames[assault.Id].ScreenThreatSquadId);
        Assert.Equal(devastator.Id, paired.Frames[assault.Id].ProtectedSquadId);
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
    public void CapabilityProfile_EffectiveEngagementRangeIsDistinctFromWeaponReach()
    {
        // Phase 2 (Design/Reference/EngagementScoringOverhaul.md): PreferredBandUpper is weapon REACH
        // (the battle-value-weighted mean of EffectiveMaximumRange). EffectiveEngagementRange is
        // the effectiveness-derived range the lookahead steers toward, and must be derived against
        // the opposing force rather than authored from the weapon's maximum.
        BattleSquad shooters = Squad("Long Reach", 81_030, 15, 0.05f);
        BattleSquad enemy = Squad("Small Target", 81_031, 10, 0.9f);
        EquipRifle(shooters.Soldiers[0], 91_030, range: 1_000, damage: 20);
        EquipMelee(enemy.Soldiers[0], 91_031);
        BattleGridManager grid = new();
        Place(grid, shooters, true, 0, 0);
        Place(grid, enemy, false, 200, 0);

        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([shooters], [enemy]);
        BattleSquadCapabilityProfile profile = paired.Profiles[shooters.Id];

        Assert.Equal(1_000f, profile.PreferredBandUpper, 3);
        Assert.InRange(profile.EffectiveEngagementRange, 0f, profile.PreferredBandUpper);
        Assert.True(
            profile.EffectiveEngagementRange < profile.PreferredBandUpper,
            $"expected the effectiveness-derived range to sit inside weapon reach, got "
                + $"{profile.EffectiveEngagementRange} vs {profile.PreferredBandUpper}");
        // PHASE 6 REPLACED THE LAST ASSERTION. It required EffectiveEngagementRange to EQUAL
        // shooters.GetPreferredEngagementRange(...) against the same representative target, which
        // was true only because the Phase 2 seam delegated to that method. Phase 6 split the two
        // deliberately: GetPreferredEngagementRange asks the effectiveness curve the UN-OPPOSED
        // question ("where am I still effective"), while EffectiveEngagementRange maximizes
        // removal(r) - incoming(r) and therefore reads what the enemy can do back. Pinning the
        // equality would pin the delegation, i.e. exactly the thing that had to go. The property
        // the test is named for -- the range is derived against the OPPOSING force, not authored
        // from the weapon -- is asserted directly instead, by making the opposition worse and
        // requiring the standoff to grow.
        //
        // The lever is toughness, not raw battle value: what pushes a shooter outward is an enemy
        // it removes SLOWLY relative to what that enemy does on arrival. Constitution 110 against a
        // damage-20 rifle puts the removal fraction near 0.15, so the melee arrival term is finally
        // the same size as the shooting, and size 8 makes the long shot worth taking.
        BattleSquad heavierThreat = Squad("Tough Big Target", 81_033, 10, 0.9f);
        ((Soldier)heavierThreat.Soldiers[0].Soldier).Constitution = 110;
        ((Soldier)heavierThreat.Soldiers[0].Soldier).Size = 8f;
        EquipMelee(heavierThreat.Soldiers[0], 91_033);
        BattleGridManager dangerousGrid = new();
        Place(dangerousGrid, shooters, true, 0, 0);
        Place(dangerousGrid, heavierThreat, false, 200, 0);
        BattleSquadCapabilityProfile againstDanger = BattleEngagementFrameBuilder
            .Build([shooters], [heavierThreat]).Profiles[shooters.Id];

        Assert.True(
            againstDanger.EffectiveEngagementRange > profile.EffectiveEngagementRange,
            "a heavier melee threat must push the derived standoff outward: "
                + $"{profile.EffectiveEngagementRange} -> "
                + $"{againstDanger.EffectiveEngagementRange}");
    }

    [Fact]
    public void CapabilityProfile_EffectiveEngagementRangeFallsBackToReachWithoutOpponents()
    {
        // The derivation needs a representative target. With no opposing force there is nothing to
        // derive from, so the quantity must degrade to reach -- i.e. to pre-Phase-2 behaviour --
        // rather than to zero, which would read as "charge".
        BattleSquad shooters = Squad("Long Reach", 81_032, 15, 0.05f);
        EquipRifle(shooters.Soldiers[0], 91_032, range: 1_000, damage: 20);
        BattleGridManager grid = new();
        Place(grid, shooters, true, 0, 0);

        BattleSquadCapabilityProfile profile =
            BattleEngagementFrameBuilder.BuildProfile(shooters);

        Assert.Equal(profile.PreferredBandUpper, profile.EffectiveEngagementRange, 3);
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
    public void Pursuit_RunPressureTapersAcrossPreferredBand()
    {
        // Run pressure is a band, not a reach cliff: zero at PreferredBandLower, half-way in
        // the middle, and full at PreferredBandUpper. The quarry is Bound so the test also
        // exercises the net-closing-speed factor used by a real withdrawal pursuit.
        float[] distances = [70, 85, 100];
        List<float> runTerms = [];
        foreach (int distance in distances)
        {
            BattleSquad pursuer = Squad("Band Pursuer", 81_250 + distance, 20, 0.05f);
            BattleSquad quarry = Squad("Band Quarry", 82_250 + distance, 10, 0.9f);
            EquipRifle(pursuer.Soldiers[0], 91_250 + distance, range: 100, damage: 20);
            EquipMelee(quarry.Soldiers[0], 92_250 + distance);
            ((Soldier)pursuer.Soldiers[0].Soldier).MoveSpeed = 8;
            ((Soldier)quarry.Soldiers[0].Soldier).MoveSpeed = 4;
            BattleGridManager grid = new();
            Place(grid, pursuer, true, 0, 0);
            Place(grid, quarry, false, distance, 0);
            Dictionary<int, EngagementRoleConstraint> constraints = new()
            {
                [pursuer.Id] = new EngagementRoleConstraint(
                    EngagementSquadRole.Pursuit,
                    QuarryRunSpeed: quarry.GetSquadMove(),
                    RoleTargets: [quarry]),
                [quarry.Id] = new EngagementRoleConstraint(EngagementSquadRole.Bound)
            };
            BattleEngagementFrameBuilder.PairedFrame paired =
                BattleEngagementFrameBuilder.Build([pursuer], [quarry], constraints);
            SquadEngagementDecision decision = Planner(grid, pursuer, quarry)
                .ChooseEngagementOption(
                    pursuer,
                    paired.Frames[pursuer.Id],
                    paired.Profiles,
                    paired.Frames,
                    [quarry],
                    [quarry]);

            runTerms.Add(decision.Candidates.Single(candidate =>
                candidate.Kind == EngagementOptionKind.RunToward).RoleTerm);
        }

        Assert.Equal(0, runTerms[0], 3);
        Assert.InRange(runTerms[1], runTerms[0] + 0.1f, runTerms[2] - 0.1f);
        Assert.True(runTerms[2] > runTerms[1]);
    }

    [Fact]
    public void Pursuit_HoldCarriesProjectedFullAimFireValue()
    {
        BattleSquad pursuer = Squad("Projected Shooter", 81_260, 20, 0.05f);
        BattleSquad quarry = Squad("Projected Quarry", 82_260, 10, 0.9f);
        EquipRifle(pursuer.Soldiers[0], 91_260, range: 500, damage: 20);
        EquipMelee(quarry.Soldiers[0], 92_260);
        ((Soldier)pursuer.Soldiers[0].Soldier).Dexterity = 20;
        ((Soldier)pursuer.Soldiers[0].Soldier).MoveSpeed = 8;
        ((Soldier)quarry.Soldiers[0].Soldier).MoveSpeed = 4;
        BattleGridManager grid = new();
        Place(grid, pursuer, true, 0, 0);
        Place(grid, quarry, false, 50, 0);
        Dictionary<int, EngagementRoleConstraint> constraints = new()
        {
            [pursuer.Id] = new EngagementRoleConstraint(
                EngagementSquadRole.Pursuit,
                QuarryRunSpeed: quarry.GetSquadMove(),
                RoleTargets: [quarry]),
            [quarry.Id] = new EngagementRoleConstraint(EngagementSquadRole.Bound)
        };
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([pursuer], [quarry], constraints);
        SquadEngagementDecision decision = Planner(grid, pursuer, quarry)
            .ChooseEngagementOption(
                pursuer,
                paired.Frames[pursuer.Id],
                paired.Profiles,
                paired.Frames,
                [quarry],
                [quarry]);

        EngagementOptionEvaluation hold = decision.Candidates.Single(candidate =>
            candidate.Kind == EngagementOptionKind.Hold);
        Assert.True(
            hold.FireWindowValue > 0,
            $"expected a projected full-aim shot to have value, got {hold.FireWindowValue}");
    }

    [Fact]
    public void Pursuit_HoldGetsNoFireWindowValueWhenQuarryRunsPastWeaponReach()
    {
        BattleSquad pursuer = Squad("Short Window Shooter", 81_270, 20, 0.05f);
        BattleSquad quarry = Squad("Short Window Quarry", 82_270, 10, 0.9f);
        EquipRifle(pursuer.Soldiers[0], 91_270, range: 100, damage: 20);
        EquipMelee(quarry.Soldiers[0], 92_270);
        ((Soldier)pursuer.Soldiers[0].Soldier).Dexterity = 20;
        ((Soldier)quarry.Soldiers[0].Soldier).MoveSpeed = 4;
        BattleGridManager grid = new();
        Place(grid, pursuer, true, 0, 0);
        Place(grid, quarry, false, 90, 0);
        Dictionary<int, EngagementRoleConstraint> constraints = new()
        {
            [pursuer.Id] = new EngagementRoleConstraint(
                EngagementSquadRole.Pursuit,
                QuarryRunSpeed: quarry.GetSquadMove(),
                RoleTargets: [quarry]),
            [quarry.Id] = new EngagementRoleConstraint(EngagementSquadRole.Bound)
        };
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([pursuer], [quarry], constraints);
        SquadEngagementDecision decision = Planner(grid, pursuer, quarry)
            .ChooseEngagementOption(
                pursuer,
                paired.Frames[pursuer.Id],
                paired.Profiles,
                paired.Frames,
                [quarry],
                [quarry]);

        EngagementOptionEvaluation hold = decision.Candidates.Single(candidate =>
            candidate.Kind == EngagementOptionKind.Hold);
        Assert.Equal(0, hold.FireWindowValue);
    }

    [Fact]
    public void Pursuit_AtMatchedSpeedPricesNoArrivalAndStandsToShoot()
    {
        // Regression for the Xibarrus Theta ambush (2026-08-04). Arrival value measured `before`
        // and `after` against the quarry's CURRENT position, so a pursuer that ran six yards
        // closer scored the whole opportunity of arriving — then the quarry spent the same turn
        // moving six yards away and the identical six yards were repriced next turn. At 6.001 vs
        // 6.001 that paid ~65 a turn, every turn, for an arrival that never came, and RunToward
        // beat standing and firing (122 vs 69) for ~997 turns until the resolver's cap.
        //
        // Netting the withdrawal out leaves nothing to buy: the squad cannot get closer, so the
        // only thing on offer is the shot it already has.
        // Contact-seeking, like the assault squad that actually did this: its desired range is
        // contact, so six yards of separation is outside it and arrival is priced at all. It
        // carries a pistol too, so standing still is a real alternative rather than a null option.
        BattleSquad pursuer = Squad("Matched Pursuer", 81_310, 20, 0.9f);
        BattleSquad quarry = Squad("Matched Quarry", 81_311, 10, 0.9f);
        EquipMelee(pursuer.Soldiers[0], 91_312);
        EquipRifle(pursuer.Soldiers[0], 91_310, range: 500, damage: 20);
        EquipMelee(quarry.Soldiers[0], 91_313);
        ((Soldier)pursuer.Soldiers[0].Soldier).MoveSpeed = 6;
        ((Soldier)quarry.Soldiers[0].Soldier).MoveSpeed = 6;
        BattleGridManager grid = new();
        Place(grid, pursuer, true, 0, 0);
        Place(grid, quarry, false, 6, 0);
        Dictionary<int, EngagementRoleConstraint> constraints = new()
        {
            [pursuer.Id] = new EngagementRoleConstraint(
                EngagementSquadRole.Pursuit,
                QuarryRunSpeed: quarry.GetSquadMove(),
                RoleTargets: [quarry]),
            // The withdrawal rate only applies against a quarry that is actually running.
            [quarry.Id] = new EngagementRoleConstraint(EngagementSquadRole.Bound)
        };
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([pursuer], [quarry], constraints);
        BattleSquadPlanner planner = Planner(grid, pursuer, quarry);

        SquadEngagementDecision decision = planner.ChooseEngagementOption(
            pursuer,
            paired.Frames[pursuer.Id],
            paired.Profiles,
            paired.Frames,
            [quarry],
            [quarry]);

        foreach (EngagementOptionEvaluation candidate in decision.Candidates)
        {
            Assert.True(
                candidate.ArrivalTimeValue == 0,
                $"{candidate.Kind} priced arrival at {candidate.ArrivalTimeValue} against a "
                    + "quarry withdrawing exactly as fast as the pursuer closes");
        }
        Assert.NotEqual(EngagementOptionKind.RunToward, decision.Chosen.Kind);
    }

    [Fact]
    public void Pursuit_AgainstASlowerQuarryStillPricesArrival()
    {
        // The paired case proving the fix turns on the speed difference and not on the Pursuit
        // role: the same geometry against a quarry it genuinely outruns still rewards closing,
        // so a real chase is untouched.
        BattleSquad pursuer = Squad("Fast Pursuer", 81_320, 20, 0.9f);
        BattleSquad quarry = Squad("Slow Quarry", 81_321, 10, 0.9f);
        EquipMelee(pursuer.Soldiers[0], 91_322);
        EquipRifle(pursuer.Soldiers[0], 91_320, range: 500, damage: 20);
        EquipMelee(quarry.Soldiers[0], 91_323);
        ((Soldier)pursuer.Soldiers[0].Soldier).MoveSpeed = 6;
        ((Soldier)quarry.Soldiers[0].Soldier).MoveSpeed = 2;
        BattleGridManager grid = new();
        Place(grid, pursuer, true, 0, 0);
        Place(grid, quarry, false, 6, 0);
        Dictionary<int, EngagementRoleConstraint> constraints = new()
        {
            [pursuer.Id] = new EngagementRoleConstraint(
                EngagementSquadRole.Pursuit,
                QuarryRunSpeed: quarry.GetSquadMove(),
                RoleTargets: [quarry]),
            [quarry.Id] = new EngagementRoleConstraint(EngagementSquadRole.Bound)
        };
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([pursuer], [quarry], constraints);
        BattleSquadPlanner planner = Planner(grid, pursuer, quarry);

        SquadEngagementDecision decision = planner.ChooseEngagementOption(
            pursuer,
            paired.Frames[pursuer.Id],
            paired.Profiles,
            paired.Frames,
            [quarry],
            [quarry]);

        Assert.Contains(
            decision.Candidates,
            candidate => candidate.ArrivalTimeValue > 0);
    }

    [Fact]
    public void MeleeOnlySquad_LongApproachRunsRatherThanJogs()
    {
        // Regression (Xibarrus Nu, 2026-08-04): two identical Abominants ~490 yards from the
        // marine line scored JogToward and RunToward within 8e-4 of each other -- `incoming` and
        // `future` both worsen slightly with proximity and very nearly cancelled the ~1e-4
        // `arrival_value` gain -- so the tie-break split them and one jogged the whole way in.
        // A squad with no ranged weapon has nothing to trade for the time it spends closing.
        BattleSquad melee = Squad("Abominant", 81_301, 60, 0.95f);
        BattleSquad shooters = Squad("Firing Line", 81_302, 40, 0.1f);
        EquipMelee(melee.Soldiers[0], 91_301);
        EquipRifle(shooters.Soldiers[0], 91_302, range: 600, damage: 20);
        BattleGridManager grid = new();
        Place(grid, melee, true, 0, 0);
        Place(grid, shooters, false, 490, 0);
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build(
                [melee], [shooters], new Dictionary<int, EngagementRoleConstraint>());
        BattleSquadPlanner planner = Planner(grid, melee, shooters);

        SquadEngagementDecision decision = planner.ChooseEngagementOption(
            melee,
            paired.Frames[melee.Id],
            paired.Profiles,
            paired.Frames,
            [shooters],
            [shooters]);

        Assert.Equal(EngagementOptionKind.RunToward, decision.Chosen.Kind);
        // The margin must come from the closing-progress role term, not from noise in the
        // exchange terms: a run covers a full stride where a jog covers half of one.
        EngagementOptionEvaluation run = decision.Candidates.Single(candidate =>
            candidate.Kind == EngagementOptionKind.RunToward);
        EngagementOptionEvaluation jog = decision.Candidates.Single(candidate =>
            candidate.Kind == EngagementOptionKind.JogToward);
        Assert.True(run.RoleTerm > jog.RoleTerm);
        Assert.True(run.Score - jog.Score > 0.01f);
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

        RangedTargetEvaluation still = planner.EvaluateRangedTarget(
            attackers.Soldiers[0], targets.Soldiers[0], weapon, 20, 0, 0);
        RangedTargetEvaluation moving = planner.EvaluateRangedTarget(
            attackers.Soldiers[0], targets.Soldiers[0], weapon, 20, 0, 4);

        Assert.True(moving.HitProbability < still.HitProbability);
        targets.Soldiers[0].CurrentSpeed = 4;
        BattleSquadPlanner liveStatePlanner = Planner(grid, attackers, targets);
        RangedTargetEvaluation live = liveStatePlanner.EvaluateRangedTarget(
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

    [Fact]
    public void ReferenceScenario_BolterSquadAt200YardsHasNonTrivialImmediateFireValue()
    {
        // Phase 3 (Design/Reference/EngagementScoringOverhaul.md). Reference scenario from the trace:
        // a ranged squad standing 200 yards from a melee-only enemy. Before Phase 3 the immediate
        // fire term was multiplied by 1/(1 + turnsUntilTargetReachesUs) -- a ~26x crush that put
        // `outgoing` four orders of magnitude below `future`, so it could never move the decision.
        // Arrival time does not affect whether a bolt lands, so the discount is gone and Hold's
        // immediate fire value is now the honest take-out-probability-weighted estimate.
        // Measured for this geometry: Hold outgoing 0.4411 before Phase 3, 22.497 after -- exactly
        // the 1/51 discount (ceil(199 / 4) = 50 turns until the enemy would have arrived).
        float outgoing = ReferenceHoldOutgoing(enemyMoveSpeed: 4);

        Assert.True(
            outgoing > 5f,
            $"Hold outgoing was {outgoing:0.#####}; the arrival-time discount "
                + "should no longer be crushing it");
    }

    [Fact]
    public void RangedRemoval_IsIndependentOfHowFastTheTargetClosesOnUs()
    {
        // Phase 3: the SAME shot at the SAME range against the SAME target must be worth the same
        // whether the enemy sprints at us or crawls. Previously a slower enemy took more turns to
        // arrive, so 1/(1 + turns) made its removal value smaller purely because of its move rate.
        float fast = ReferenceHoldOutgoing(enemyMoveSpeed: 8);
        float slow = ReferenceHoldOutgoing(enemyMoveSpeed: 1);

        Assert.True(fast > 0, $"expected positive immediate fire value, got {fast:0.#####}");
        Assert.Equal(fast, slow, 5);
    }

    [Fact]
    public void RangedRemoval_AgainstWithdrawingTargetIsStillWorthSomething()
    {
        // Phase 3: a withdrawing target has turnsUntilTargetReachesUs = infinity, which drove the
        // old discount -- and therefore the entire immediate ranged value -- to exactly zero. A
        // retreating enemy is still a legal, damageable target.
        float engaged = ReferenceHoldOutgoing(enemyMoveSpeed: 4);
        float withdrawing = ReferenceHoldOutgoing(
            enemyMoveSpeed: 4, withdrawalRole: WithdrawalRole.Bound);

        Assert.True(withdrawing > 0,
            $"expected a withdrawing enemy to still be worth shooting, got {withdrawing:0.#####}");
        Assert.Equal(engaged, withdrawing, 5);
    }

    [Fact]
    public void MeleeChargePayoff_StillDiscountsByTurnsUntilWeReachContact()
    {
        // Phase 3 retains the arrival discount where it is genuinely correct: the melee charge
        // payoff in EstimateChargeNet really is deferred until contact. A charger already in
        // contact (turnsToContact 0) collects full value; one that needs a turn of movement
        // collects 1/(1 + 1) = half.
        float atContact = ReferenceChargeMeleeNow(separation: 1);
        float oneTurnAway = ReferenceChargeMeleeNow(separation: 5);

        Assert.True(atContact > 0, $"expected charge value at contact, got {atContact:0.#####}");
        Assert.Equal(atContact * 0.5f, oneTurnAway, 4);
    }

    [Fact]
    public void ContactSeekingSquad_ClosesAtEveryDistance()
    {
        foreach (int distance in new[] { 2, 7, 40, 120, 418, 900 })
        {
            BattleSquad melee = Squad("Contact Seekers", 81_140 + distance, 10, 0.95f);
            BattleSquad enemy = Squad("Ranged Enemy", 82_140 + distance, 10, 0.05f);
            EquipMelee(melee.Soldiers[0], 91_140 + distance);
            EquipRifle(enemy.Soldiers[0], 92_140 + distance, 2_000, 10);
            BattleGridManager grid = new();
            Place(grid, melee, true, 0, 0);
            Place(grid, enemy, false, distance, 0);
            BattleEngagementFrameBuilder.PairedFrame paired =
                BattleEngagementFrameBuilder.Build([melee], [enemy]);
            SquadEngagementDecision decision = Planner(grid, melee, enemy)
                .ChooseEngagementOption(
                    melee,
                    paired.Frames[melee.Id],
                    paired.Profiles,
                    paired.Frames,
                    [melee],
                    [enemy]);

            Assert.DoesNotContain(
                decision.Chosen.Kind,
                new[] { EngagementOptionKind.Hold, EngagementOptionKind.StepBack });
            Assert.True(
                decision.Chosen.FeasibleSpeed > 0,
                $"distance={distance} chose {decision.Chosen.Kind} without forward motion");
        }
    }

    [Fact]
    public void ContactSeekingSquad_ValuesFasterArrivalWhenContactExchangeIsUseful()
    {
        BattleSquad melee = Squad("Long Charge", 81_145, 20, 0.95f);
        BattleSquad enemy = Squad("Unarmed Target", 82_145, 1, 0.05f);
        EquipMelee(melee.Soldiers[0], 91_145);
        BattleGridManager grid = new();
        Place(grid, melee, true, 0, 0);
        Place(grid, enemy, false, 418, 0);

        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([melee], [enemy]);
        SquadEngagementDecision decision = Planner(grid, melee, enemy)
            .ChooseEngagementOption(
                melee,
                paired.Frames[melee.Id],
                paired.Profiles,
                paired.Frames,
                [melee],
                [enemy]);

        EngagementOptionEvaluation walk = decision.Candidates.Single(
            candidate => candidate.Kind == EngagementOptionKind.StepForward);
        EngagementOptionEvaluation jog = decision.Candidates.Single(
            candidate => candidate.Kind == EngagementOptionKind.JogToward);
        EngagementOptionEvaluation run = decision.Candidates.Single(
            candidate => candidate.Kind == EngagementOptionKind.RunToward);

        Assert.True(
            run.ArrivalTimeValue > jog.ArrivalTimeValue
                && jog.ArrivalTimeValue > walk.ArrivalTimeValue,
            $"expected faster arrival to be worth more: "
                + $"walk={walk.ArrivalTimeValue}, jog={jog.ArrivalTimeValue}, "
                + $"run={run.ArrivalTimeValue}");
        Assert.Equal(EngagementOptionKind.RunToward, decision.Chosen.Kind);
    }

    [Fact]
    public void ContactSeekingSquad_ChargeAdvantageIsMonotoneInRange()
    {
        List<float> advantages = [];
        foreach (int distance in new[] { 120, 40, 7 })
        {
            BattleSquad hybrid = Squad("Hybrid Chargers", 81_150 + distance, 10, 0.9f);
            BattleSquad enemy = Squad("Ranged Enemy", 82_150 + distance, 10, 0.05f);
            EquipMelee(hybrid.Soldiers[0], 91_150 + distance);
            EquipRifle(hybrid.Soldiers[0], 92_150 + distance, 2_000, 10);
            EquipRifle(enemy.Soldiers[0], 93_150 + distance, 2_000, 10);
            BattleGridManager grid = new();
            Place(grid, hybrid, true, 0, 0);
            Place(grid, enemy, false, distance, 0);
            BattleEngagementFrameBuilder.PairedFrame paired =
                BattleEngagementFrameBuilder.Build([hybrid], [enemy]);
            SquadEngagementDecision decision = Planner(grid, hybrid, enemy)
                .ChooseEngagementOption(
                    hybrid,
                    paired.Frames[hybrid.Id],
                    paired.Profiles,
                    paired.Frames,
                    [hybrid],
                    [enemy]);
            EngagementOptionEvaluation hold = decision.Candidates.Single(
                candidate => candidate.Kind == EngagementOptionKind.Hold);
            EngagementOptionEvaluation charge = decision.Candidates.Single(
                candidate => candidate.Kind == EngagementOptionKind.CloseToContact);
            advantages.Add(charge.Score - hold.Score);
        }

        Assert.True(
            advantages[1] >= advantages[0] - 0.15f,
            $"charge advantage fell from 120 to 40 yards: {string.Join(", ", advantages)}");
        Assert.True(
            advantages[2] >= advantages[1] - 0.15f,
            $"charge advantage fell from 40 to 7 yards: {string.Join(", ", advantages)}");
    }

    [Fact]
    public void ContactSeekingSquad_MeleeTermIsNonZeroBeyondOneMove()
    {
        BattleSquad hybrid = Squad("Long Charge", 81_160, 10, 0.9f);
        BattleSquad enemy = Squad("Ranged Enemy", 82_160, 10, 0.05f);
        EquipMelee(hybrid.Soldiers[0], 91_160);
        EquipRifle(hybrid.Soldiers[0], 92_160, 2_000, 10);
        EquipRifle(enemy.Soldiers[0], 93_160, 2_000, 10);
        BattleGridManager grid = new();
        Place(grid, hybrid, true, 0, 0);
        Place(grid, enemy, false, 40, 0);
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([hybrid], [enemy]);
        SquadEngagementDecision decision = Planner(grid, hybrid, enemy)
            .ChooseEngagementOption(
                hybrid,
                paired.Frames[hybrid.Id],
                paired.Profiles,
                paired.Frames,
                [hybrid],
                [enemy]);

        EngagementOptionEvaluation charge = decision.Candidates.Single(
            candidate => candidate.Kind == EngagementOptionKind.CloseToContact);
        Assert.True(charge.MeleeNow > 0, $"long charge melee term was {charge.MeleeNow}");
    }

    [Fact]
    public void RangedSquadInBand_HoldsAgainstAnApproachingMeleeEnemy()
    {
        BattleSquad ranged = Squad("Band Shooters", 81_170, 10, 0.05f);
        BattleSquad melee = Squad("Approaching Melee", 82_170, 10, 0.95f);
        EquipRifle(ranged.Soldiers[0], 91_170, 200, 10);
        EquipMelee(melee.Soldiers[0], 92_170);
        BattleGridManager grid = new();
        Place(grid, ranged, true, 0, 0);
        Place(grid, melee, false, 150, 0);
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([ranged], [melee]);
        SquadEngagementDecision decision = Planner(grid, ranged, melee)
            .ChooseEngagementOption(
                ranged,
                paired.Frames[ranged.Id],
                paired.Profiles,
                paired.Frames,
                [ranged],
                [melee]);

        Assert.Equal(
            EngagementOptionKind.Hold,
            decision.Chosen.Kind);
    }

    [Fact]
    public void RangedSquadOutOfBand_ClosesTowardItsBand()
    {
        BattleSquad ranged = Squad("Out Of Band Shooters", 81_180, 10, 0.05f);
        BattleSquad melee = Squad("Approaching Melee", 82_180, 10, 0.95f);
        EquipRifle(ranged.Soldiers[0], 91_180, 200, 10);
        EquipMelee(melee.Soldiers[0], 92_180);
        BattleGridManager grid = new();
        Place(grid, ranged, true, 0, 0);
        Place(grid, melee, false, 240, 0);
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([ranged], [melee]);
        SquadEngagementDecision decision = Planner(grid, ranged, melee)
            .ChooseEngagementOption(
                ranged,
                paired.Frames[ranged.Id],
                paired.Profiles,
                paired.Frames,
                [ranged],
                [melee]);

        Assert.DoesNotContain(
            decision.Chosen.Kind,
            new[] { EngagementOptionKind.Hold, EngagementOptionKind.StepBack });
        Assert.True(decision.Chosen.FeasibleSpeed > 0);
    }

    [Fact]
    public void EveryOption_IsDistinguishableOutsideTheIndifferenceBand()
    {
        // Nu-8, turn 1: a 20-strong Acolyte Hybrid squad -- autopistol plus rending claw,
        // authored melee fraction 0.75 -- sat 522 yards from a Space Marine gun line and chose
        // StepBack for thirty consecutive turns. Hold, StepBack and StepForward scored within
        // 0.010 of each other against an indifference band of max(0.1, BV * 0.02) = 3.2, so the
        // retreat was float noise resolved by posture stickiness rather than a decision.
        //
        // THE INVARIANT. The options surviving the indifference filter -- exactly the set
        // ChooseEngagementOption tie-breaks among -- must never contain BOTH a withdrawal and a
        // closing option. When the score genuinely cannot tell advancing from retreating,
        // doctrine has to break the tie before rounding does.
        // Lethality is swept rather than pinned. A single hand-tuned pistol can be nudged until
        // it happens to sit outside the band, which would test the tuning and not the invariant;
        // the claim is that NO sidearm strength should leave advance and retreat interchangeable.
        // Range 350 and degrading damage are the real Autopistol's; armour 20 is Mk VII.
        foreach ((int distance, int damage) in
            new[] { (418, 3), (418, 12), (418, 25), (522, 3), (522, 12), (522, 25) })
        {
            int key = distance + damage;
            // Twenty strong, as the reported squad was. Size is load-bearing: the indifference
            // band is BV * 0.02, so it grows with the roster while the score differences between
            // standing, stepping back and stepping forward do not. A one-soldier fixture has a
            // 0.16 band and cannot show this at all.
            BattleSquad hybrid = Squad(
                "Pistol Hybrids",
                Enumerable.Range(0, 20)
                    .Select(index => (81_190 + (key * 40) + index, 8))
                    .ToList(),
                0.75f);
            // Thirty bolter marines, as in the report. The size of the gun line is what makes the
            // crossing cost large enough to bury CloseToContact; against a single shooter the
            // charge simply wins and the noise never gets to decide anything.
            BattleSquad marines = Squad(
                "Armored Gun Line",
                Enumerable.Range(0, 30)
                    .Select(index => (82_190 + (key * 40) + index, 20))
                    .ToList(),
                0.05f);
            foreach (BattleSoldier member in hybrid.Soldiers)
            {
                EquipMelee(member, 91_190 + (key * 40) + (member.Soldier.Id % 40));
                EquipPistol(
                    member, 92_190 + (key * 40) + (member.Soldier.Id % 40), 350, damage);
            }
            foreach (BattleSoldier member in marines.Soldiers)
            {
                // Marine-grade marksmanship, as ReferenceBolterSquad does: a gun line that cannot
                // actually hit does not make the approach expensive, and the expense of the
                // approach is the whole mechanism under test.
                ((Soldier)member.Soldier).Dexterity = 20;
                EquipRifle(member, 93_190 + (key * 40) + (member.Soldier.Id % 40), 1_000, 20);
                member.Armor = new Armor(new ArmorTemplate(
                    94_190 + (key * 40) + (member.Soldier.Id % 40),
                    "Astartes Power Armor Mk VII",
                    20,
                    0));
            }
            BattleGridManager grid = new();
            for (int index = 0; index < hybrid.Soldiers.Count; index++)
            {
                Place(grid, hybrid.Soldiers[index], true, 0, index);
            }
            for (int index = 0; index < marines.Soldiers.Count; index++)
            {
                Place(grid, marines.Soldiers[index], false, distance, index);
            }
            BattleEngagementFrameBuilder.PairedFrame paired =
                BattleEngagementFrameBuilder.Build([hybrid], [marines]);
            SquadEngagementDecision decision = Planner(grid, hybrid, marines)
                .ChooseEngagementOption(
                    hybrid,
                    paired.Frames[hybrid.Id],
                    paired.Profiles,
                    paired.Frames,
                    [hybrid],
                    [marines]);

            float best = decision.Candidates.Max(candidate => candidate.Score);
            // Mirrors EngagementIndifferenceFraction; kept literal so a change to the constant
            // has to be made deliberately here too.
            float indifference = System.Math.Max(
                0.1f, paired.Profiles[hybrid.Id].TotalAbleBattleValue * 0.02f);
            List<EngagementOptionKind> inBand = decision.Candidates
                .Where(candidate => best - candidate.Score <= indifference)
                .Select(candidate => candidate.Kind)
                .ToList();

            Assert.False(
                inBand.Contains(EngagementOptionKind.StepBack) && inBand.Any(IsClosing),
                $"distance={distance} damage={damage}: withdrawal and closing are "
                    + $"indistinguishable inside the indifference band "
                    + $"({string.Join(", ", inBand)})");
        }
    }

    [Fact]
    public void FasterPursuer_ClosesMonotonicallyWithoutOscillating()
    {
        // The "chasing loops" case. Each iteration advances the pursuer by the chosen option's
        // own feasible speed and re-plans from there, which exercises planner consistency across
        // an approach without running turn resolution. LastEngagementOptionKind is deliberately
        // left unset: stickiness must not be what hides an oscillation.
        BattleSquad pursuer = Squad("Fast Pursuer", 81_195, 10, 0.95f);
        BattleSquad quarry = Squad("Slow Quarry", 82_195, 10, 0.05f);
        EquipMelee(pursuer.Soldiers[0], 91_195);
        EquipRifle(quarry.Soldiers[0], 92_195, 2_000, 10);
        ((Soldier)pursuer.Soldiers[0].Soldier).MoveSpeed = 10;
        ((Soldier)quarry.Soldiers[0].Soldier).MoveSpeed = 3;
        BattleGridManager grid = new();
        Place(grid, pursuer, true, 0, 0);
        Place(grid, quarry, false, 200, 0);

        List<float> ranges = [];
        List<EngagementOptionKind> postures = [];
        int position = 0;
        for (int turn = 0; turn < 6; turn++)
        {
            BattleEngagementFrameBuilder.PairedFrame paired =
                BattleEngagementFrameBuilder.Build([pursuer], [quarry]);
            SquadEngagementDecision decision = Planner(grid, pursuer, quarry)
                .ChooseEngagementOption(
                    pursuer,
                    paired.Frames[pursuer.Id],
                    paired.Profiles,
                    paired.Frames,
                    [pursuer],
                    [quarry]);
            postures.Add(decision.Chosen.Kind);
            position = System.Math.Min(199, position
                + (int)System.Math.Round(decision.Chosen.FeasibleSpeed));
            grid.RemoveSoldier(pursuer.Soldiers[0].Soldier.Id);
            Place(grid, pursuer, true, position, 0);
            ranges.Add(200 - position);
        }

        for (int index = 1; index < ranges.Count; index++)
        {
            Assert.True(
                ranges[index] < ranges[index - 1],
                $"range did not fall on turn {index + 1}: "
                    + $"{string.Join(", ", ranges)} via {string.Join(", ", postures)}");
        }
        for (int index = 2; index < postures.Count; index++)
        {
            Assert.False(
                postures[index] == postures[index - 2]
                    && postures[index] != postures[index - 1],
                $"posture oscillated: {string.Join(", ", postures)}");
        }
    }

    [Fact]
    public void MixedPistolAndBladeSquad_ScreensTheGunLineAcrossTheApproach()
    {
        // D7, and the regression guard for MinimumScreenClosingRate. Every SCREEN_EVAL row in the
        // reference battle was rejected because the threat's closing rate
        // (MoveSpeed 6 / 418 yards = 0.01435) sat just under the old 0.015 gate. A threat four
        // hundred yards out and walking is still the threat the assault squad exists to
        // intercept, so the long approach is asserted alongside the short one -- the gate has to
        // stay below it.
        //
        // The plan's sketch of this test also asked for a planned shot. That turned out to be
        // over-specified: at the ranges where a screen is live the planner prefers CloseToContact
        // over MoveToInterpose (50.33 vs 50.21 in this fixture), and charging the threat IS
        // screening the gun line. Asserting a shot would pin an arbitrary side of a near-tie
        // between two options that both discharge the doctrine. What is asserted instead is that
        // the screen never answers the threat by standing still or giving ground.
        foreach (int distance in new[] { 418, 40 })
        {
            BattleSquad assault = Squad("Pistol And Blade", 81_200 + distance, 10, 0.75f);
            BattleSquad gunLine = Squad("Gun Line", 82_200 + distance, 20, 0.05f);
            BattleSquad threat = Squad("Melee Threat", 83_200 + distance, 12, 0.95f);
            EquipMelee(assault.Soldiers[0], 91_200 + distance);
            EquipPistol(assault.Soldiers[0], 92_200 + distance, range: 50, damage: 12);
            EquipRifle(gunLine.Soldiers[0], 93_200 + distance, 1_000, 20);
            EquipMelee(threat.Soldiers[0], 94_200 + distance);
            ((Soldier)threat.Soldiers[0].Soldier).MoveSpeed = 6;
            BattleGridManager grid = new();
            // The screen starts roughly on the line it is meant to hold, so taking the interpose
            // posture is a short adjustment rather than a sprint. That distinction is the point:
            // InterposeTier is distance-based, and a squad that has to dash across the field to
            // screen genuinely cannot fire on the way. Screening while shooting is what an
            // already-positioned assault squad does.
            Place(grid, assault, true, (distance - 3) / 2, 10);
            Place(grid, gunLine, true, 0, 10);
            Place(grid, threat, false, distance, 10);

            BattleEngagementFrameBuilder.PairedFrame paired =
                BattleEngagementFrameBuilder.Build([assault, gunLine], [threat]);
            SquadEngagementFrame frame = paired.Frames[assault.Id];

            Assert.Equal(threat.Id, frame.ScreenThreatSquadId);
            Assert.Equal(gunLine.Id, frame.ProtectedSquadId);
            Assert.NotNull(frame.InterposePoint);

            SquadEngagementDecision decision = Planner(grid, assault, gunLine, threat)
                .ChooseEngagementOption(
                    assault,
                    frame,
                    paired.Profiles,
                    paired.Frames,
                    [assault, gunLine],
                    [threat]);

            Assert.True(
                decision.Chosen.Kind == EngagementOptionKind.MoveToInterpose
                    || IsClosing(decision.Chosen.Kind),
                $"distance={distance}: a screen answered the threat with "
                    + $"{decision.Chosen.Kind}");
        }
    }

    [Fact]
    public void PeakRangedRemovalFraction_SeparatesARealGunFromASidearm()
    {
        // Calibration guard for ContactSeekerRangedRelevanceFraction. The threshold is only
        // defensible if the two populations it separates are far apart; measured here they are
        // three orders of magnitude apart, so the constant can move a long way without
        // reclassifying anything. If this gap ever closes, the mask is being asked to make a
        // judgement the underlying quantity cannot support, and the constant is the wrong tool.
        BattleSquad rifleman = Squad("Rifle Hybrid", 71_000, 10, 0.9f);
        BattleSquad softEnemy = Squad("Unarmored Enemy", 72_000, 10, 0.05f);
        EquipMelee(rifleman.Soldiers[0], 75_001);
        EquipRifle(rifleman.Soldiers[0], 75_002, 2_000, 10);
        EquipRifle(softEnemy.Soldiers[0], 73_000, 2_000, 10);
        BattleGridManager softGrid = new();
        Place(softGrid, rifleman, true, 0, 0);
        Place(softGrid, softEnemy, false, 40, 0);

        BattleSquad pistolier = Squad("Pistol Hybrid", 71_001, 10, 0.9f);
        BattleSquad armoredEnemy = Squad("Armored Enemy", 72_001, 10, 0.05f);
        EquipMelee(pistolier.Soldiers[0], 75_005);
        EquipPistol(pistolier.Soldiers[0], 75_006, 350, 3);
        EquipRifle(armoredEnemy.Soldiers[0], 73_001, 2_000, 10);
        ((Soldier)armoredEnemy.Soldiers[0].Soldier).Dexterity = 20;
        armoredEnemy.Soldiers[0].Armor = new Armor(
            new ArmorTemplate(74_001, "Astartes Power Armor Mk VII", 20, 0));
        BattleGridManager armoredGrid = new();
        Place(armoredGrid, pistolier, true, 0, 0);
        Place(armoredGrid, armoredEnemy, false, 418, 0);

        float realGun = BattleEngagementFrameBuilder
            .Build([rifleman], [softEnemy]).Profiles[rifleman.Id].PeakRangedRemovalFraction;
        float sidearm = BattleEngagementFrameBuilder
            .Build([pistolier], [armoredEnemy]).Profiles[pistolier.Id].PeakRangedRemovalFraction;

        Assert.True(
            realGun > sidearm * 100f,
            $"a real gun ({realGun:0.######}) must outclass a useless sidearm "
                + $"({sidearm:0.######}) by orders of magnitude, not by a margin");
        Assert.True(realGun > 0.1f, $"a working rifle scored {realGun:0.######}");
        Assert.True(sidearm < 0.01f, $"a pistol against power armour scored {sidearm:0.######}");
    }

    private static bool IsClosing(EngagementOptionKind kind) =>
        kind is EngagementOptionKind.StepForward
            or EngagementOptionKind.JogToward
            or EngagementOptionKind.RunToward
            or EngagementOptionKind.CloseToContact;

    private static float ReferenceHoldOutgoing(
        float enemyMoveSpeed,
        WithdrawalRole withdrawalRole = WithdrawalRole.None)
    {
        BattleSquad shooters = ReferenceBolterSquad(out RangedWeapon rifle);
        // A big melee monster (Carnifex-shaped: size 8) is the trace's actual target -- and the
        // only kind of target a rifleman can plausibly hit at 200 yards, so the scenario exercises
        // the arrival discount rather than the hit-probability floor.
        BattleSquad meleeEnemy = Squad("Reference Melee Enemy", 81_121, 30, 0.95f);
        EquipMelee(meleeEnemy.Soldiers[0], 91_121);
        ((Soldier)meleeEnemy.Soldiers[0].Soldier).Size = 8;
        ((Soldier)meleeEnemy.Soldiers[0].Soldier).MoveSpeed = enemyMoveSpeed;
        meleeEnemy.WithdrawalRole = withdrawalRole;
        // Already settled and fully aimed, so Hold plans the shot rather than another turn of
        // aiming -- otherwise `outgoing` is zero for reasons unrelated to the arrival discount.
        shooters.Soldiers[0].Aim = (meleeEnemy.Soldiers[0].Soldier.Id, rifle, 3);
        BattleGridManager grid = new();
        Place(grid, shooters, true, 0, 0);
        Place(grid, meleeEnemy, false, 200, 0);
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([shooters], [meleeEnemy]);
        BattleSquadPlanner planner = Planner(grid, shooters, meleeEnemy);

        SquadEngagementDecision decision = planner.ChooseEngagementOption(
            shooters, paired.Frames[shooters.Id], paired.Profiles, paired.Frames,
            [shooters], [meleeEnemy]);
        return decision.Candidates
            .Single(candidate => candidate.Kind == EngagementOptionKind.Hold)
            .ImmediateEnemyRemoval;
    }

    private static float ReferenceChargeMeleeNow(int separation)
    {
        BattleSquad chargers = Squad("Chargers", 81_130, 20, 0.9f);
        BattleSquad enemy = Squad("Charge Target", 81_131, 20, 0.9f);
        EquipMelee(chargers.Soldiers[0], 91_130);
        EquipMelee(enemy.Soldiers[0], 91_131);
        ((Soldier)chargers.Soldiers[0].Soldier).MoveSpeed = 8;
        BattleGridManager grid = new();
        Place(grid, chargers, true, 0, 0);
        Place(grid, enemy, false, separation, 0);
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([chargers], [enemy]);
        BattleSquadPlanner planner = Planner(grid, chargers, enemy);

        SquadEngagementDecision decision = planner.ChooseEngagementOption(
            chargers, paired.Frames[chargers.Id], paired.Profiles, paired.Frames,
            [chargers], [enemy]);
        return decision.Candidates
            .Single(candidate => candidate.Kind == EngagementOptionKind.CloseToContact)
            .MeleeNow;
    }

    private static BattleSquad ReferenceBolterSquad(out RangedWeapon rifle)
    {
        BattleSquad shooters = Squad("Reference Bolters", 81_120, 9, 0.05f);
        // Marine-grade marksmanship: the point of the scenario is the arrival-time discount, so the
        // shot must clear the hit-probability floor and be worth taking rather than aiming.
        ((Soldier)shooters.Soldiers[0].Soldier).Dexterity = 20;
        rifle = EquipRifle(shooters.Soldiers[0], 91_120, range: 1_000, damage: 20);
        return shooters;
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

    /// <summary>
    /// A sidearm: one-handed, short-ranged, and unlike <see cref="EquipRifle"/> it leaves any
    /// melee weapon in place. That combination is the whole point -- a squad carrying both is the
    /// mixed contact-seeker the option mask has to reason about.
    /// </summary>
    private static RangedWeapon EquipPistol(
        BattleSoldier soldier,
        int id,
        float range,
        float damage)
    {
        RangedWeapon weapon = new(new RangedWeaponTemplate(
            id,
            "Planning Pistol",
            EquipLocation.OneHand,
            TestSkills.Ranged,
            accuracy: 3,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: damage,
            maxDistance: range,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 1,
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
