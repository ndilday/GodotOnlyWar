using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Tests.Battles;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class BattleTurnResolverWithdrawalTests
{
    [Fact]
    public void ProcessNextTurn_RecordsTypedAnnihilationForInitiallyEliminatedSide()
    {
        BattleSquad attackers = CreateSquad("Eliminated", 73_001, TestModelFactory.MarineTemplate);
        BattleSquad defenders = CreateSquad("Holder", 73_002, TestModelFactory.MarineTemplate);
        attackers.Soldiers[0].TopLeft = (0, 0);
        attackers.Status = BattleSquadStatus.Eliminated;
        BattleGridManager grid = new();
        Place(grid, defenders.Soldiers[0], false, 20, 0);
        BattleTurnResolver resolver = CreateResolver(
            grid,
            [attackers],
            [defenders],
            Aggression.Aggressive,
            Aggression.Aggressive);

        resolver.ProcessNextTurn();

        Assert.NotNull(resolver.BattleHistory.Outcome);
        Assert.Equal(BattleEndReason.Annihilation, resolver.BattleHistory.Outcome.EndReason);
        Assert.Equal(BattleSide.Opposing, resolver.BattleHistory.Outcome.SideHoldingField);
        Assert.Contains(attackers.Id, resolver.BattleHistory.Outcome.EliminatedSquadIds);
        Assert.Empty(resolver.BattleHistory.Outcome.DisengagedSquadIds);
    }

    [Fact]
    public void ProcessNextTurn_VoluntaryWithdrawalWithBreakOffEmitsTypedEventsAndOutcome()
    {
        SoldierTemplate zeroValueHuman = new(
            73_010,
            TestModelFactory.HumanSpecies,
            "Zero Value Human",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 0);
        BattleSquad withdrawing = CreateSquad("Withdrawing", 73_011, zeroValueHuman);
        BattleSquad holder = CreateSquad(
            "Holder",
            73_012,
            TestModelFactory.MarineTemplate,
            isPlayerSquad: true);
        BattleGridManager grid = new();
        Place(grid, withdrawing.Soldiers[0], true, 0, 0);
        Place(grid, holder.Soldiers[0], false, 1_000, 0);
        BattleTurnResolver resolver = CreateResolver(
            grid,
            [withdrawing],
            [holder],
            Aggression.Normal,
            Aggression.Avoid);

        resolver.ProcessNextTurn();

        BattleOutcome outcome = Assert.IsType<BattleOutcome>(resolver.BattleHistory.Outcome);
        Assert.Equal(BattleEndReason.Withdrawal, outcome.EndReason);
        Assert.Equal(BattleSide.Opposing, outcome.SideHoldingField);
        Assert.Contains(withdrawing.Id, outcome.DisengagedSquadIds);
        IReadOnlyList<BattleEvent> events = resolver.BattleHistory.Turns.Last().Events;
        Assert.Contains(events, battleEvent =>
            battleEvent.Type == BattleEventType.WithdrawalOrdered
            && battleEvent.Side == BattleSide.Attacker
            && battleEvent.Description == "Opposing force ordered a fighting withdrawal.");
        Assert.Contains(events, battleEvent =>
            battleEvent.Type == BattleEventType.PursuitEnded
            && battleEvent.Side == BattleSide.Opposing
            && battleEvent.Description == "Player force declined pursuit.");
        Assert.Contains(events, battleEvent =>
            battleEvent.Type == BattleEventType.SquadDisengaged
            && battleEvent.PrimarySquadId == withdrawing.Id);
        Assert.Contains(events, battleEvent =>
            battleEvent.Type == BattleEventType.ForceDisengaged
            && battleEvent.Side == BattleSide.Attacker);
    }

    [Fact]
    public void ProcessNextTurn_BurrowCapableWithdrawingSquadDisengagesImmediately()
    {
        // Valueless so the force evaluator elects to withdraw, the same way the BreakOff case
        // above does — stated locally rather than leaning on a fixture's BattleValue, so the
        // premise of the test is visible at the point of use.
        SoldierTemplate zeroValueBurrower = new(
            73_020,
            TestModelFactory.BurrowerSpecies,
            "Zero Value Burrower",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 0);
        BattleSquad burrowers = CreateSquad(
            "Burrowers",
            73_021,
            zeroValueBurrower);
        BattleSquad pursuers = CreateSquad("Pursuers", 73_022, TestModelFactory.MarineTemplate);
        BattleGridManager grid = new();
        Place(grid, burrowers.Soldiers[0], true, 0, 0);
        Place(grid, pursuers.Soldiers[0], false, 1_000, 0);
        BattleTurnResolver resolver = CreateResolver(
            grid,
            [burrowers],
            [pursuers],
            Aggression.Normal,
            Aggression.Normal);

        resolver.ProcessNextTurn();

        BattleOutcome outcome = Assert.IsType<BattleOutcome>(resolver.BattleHistory.Outcome);
        Assert.Equal(BattleEndReason.Withdrawal, outcome.EndReason);
        Assert.Contains(burrowers.Id, outcome.DisengagedSquadIds);
        BattleEvent disengagement = Assert.Single(
            resolver.BattleHistory.Turns.Last().Events,
            battleEvent => battleEvent.Type == BattleEventType.SquadDisengaged);
        Assert.Equal(burrowers.Id, disengagement.PrimarySquadId);
        Assert.Contains("burrowing capability", disengagement.Description);
    }

    [Fact]
    public void ProcessNextTurn_LongBarrelledPursuerBreaksOffWhenItCannotUsefullyShoot()
    {
        // Regression for the Xibarrus Nu ambush (2026-07-30). The pursuit gates used to ask "can we
        // shoot from here?" and answer with the longest weapon's MAXIMUM range. One Heavy Bolter at
        // 1600 yards made a force look able to shoot from anywhere, so an Aggressive pursuer that
        // could neither close nor land anything kept the engagement alive for hundreds of turns.
        // The weapon here reaches 2000 yards on paper and is useless at any of them, so the gates
        // must now report no worthwhile shot and let the withdrawal succeed.
        SoldierTemplate zeroValueHuman = new(
            73_030,
            TestModelFactory.HumanSpecies,
            "Zero Value Human",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 0);
        BattleSquad withdrawing = CreateSquad("Withdrawing", 73_031, zeroValueHuman);
        BattleSquad pursuer = CreateSquad(
            "Pursuer",
            73_032,
            TestModelFactory.MarineTemplate,
            isPlayerSquad: true);
        EquipLongRangedPeashooter(pursuer.Soldiers[0]);
        BattleGridManager grid = new();
        Place(grid, withdrawing.Soldiers[0], true, 0, 0);
        Place(grid, pursuer.Soldiers[0], false, 400, 0);
        BattleTurnResolver resolver = CreateResolver(
            grid,
            [withdrawing],
            [pursuer],
            Aggression.Normal,
            Aggression.Aggressive);

        resolver.ProcessNextTurn();

        BattleOutcome outcome = Assert.IsType<BattleOutcome>(resolver.BattleHistory.Outcome);
        Assert.Equal(BattleEndReason.Withdrawal, outcome.EndReason);
        Assert.Contains(withdrawing.Id, outcome.DisengagedSquadIds);
        Assert.Contains(
            resolver.BattleHistory.Turns.Last().Events,
            battleEvent => battleEvent.Type == BattleEventType.PursuitEnded);
    }

    [Fact]
    public void ProcessNextTurn_PursuerWithRealReachKeepsTheEngagementAlive()
    {
        // The paired case that proves the test above turns on the weapon rather than the distance.
        // Identical geometry and aggression; only the gun differs. This one can genuinely hit and
        // hurt at 400 yards, so there IS a worthwhile shot and the pursuit continues.
        SoldierTemplate zeroValueHuman = new(
            73_050,
            TestModelFactory.HumanSpecies,
            "Zero Value Human",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 0);
        BattleSquad withdrawing = CreateSquad("Withdrawing", 73_051, zeroValueHuman);
        BattleSquad pursuer = CreateSquad(
            "Pursuer",
            73_052,
            TestModelFactory.MarineTemplate,
            isPlayerSquad: true);
        EquipAccurateLongGun(pursuer.Soldiers[0]);
        BattleGridManager grid = new();
        Place(grid, withdrawing.Soldiers[0], true, 0, 0);
        Place(grid, pursuer.Soldiers[0], false, 400, 0);
        BattleTurnResolver resolver = CreateResolver(
            grid,
            [withdrawing],
            [pursuer],
            Aggression.Normal,
            Aggression.Aggressive);

        resolver.ProcessNextTurn();

        Assert.Null(resolver.BattleHistory.Outcome);
    }

    [Fact]
    public void ProcessNextTurn_UnpursuedBoundBeyondRelativeRetargetHorizonDisengages()
    {
        SoldierTemplate zeroValueHuman = new(
            73_070,
            TestModelFactory.HumanSpecies,
            "Zero Value Runner",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 0);
        BattleSquad firstRunner = CreateSquad("First Runner", 73_071, zeroValueHuman);
        BattleSquad secondRunner = CreateSquad("Second Runner", 73_072, zeroValueHuman);
        BattleSquad pursuer = CreateSquad(
            "Single Pursuer",
            73_073,
            TestModelFactory.MarineTemplate,
            isPlayerSquad: true);
        foreach (BattleSquad runner in new[] { firstRunner, secondRunner })
        {
            ((Soldier)runner.Soldiers[0].Soldier).MoveSpeed = 6;
        }
        ((Soldier)pursuer.Soldiers[0].Soldier).MoveSpeed = 8;
        pursuer.Soldiers[0].RangedWeapons.Clear();
        pursuer.Soldiers[0].ClearReadiedRangedWeapons();

        BattleGridManager grid = new();
        Place(grid, firstRunner.Soldiers[0], true, 0, 0);
        Place(grid, secondRunner.Soldiers[0], true, 0, 20);
        Place(grid, pursuer.Soldiers[0], false, 40, 10);
        BattleTurnResolver resolver = CreateResolver(
            grid,
            [firstRunner, secondRunner],
            [pursuer],
            Aggression.Normal,
            Aggression.Aggressive);

        // The valueless side elects to withdraw on turn one. On turn two the single pursuer can
        // select only one squad as its quarry; the other is well beyond the two-turn retarget
        // horizon at a relative closing speed of two.
        resolver.ProcessNextTurn();
        Assert.Null(resolver.BattleHistory.Outcome);
        resolver.ProcessNextTurn();

        IReadOnlyList<BattleEvent> events = resolver.BattleHistory.Turns.Last().Events;
        BattleEvent escaped = Assert.Single(events, battleEvent =>
            battleEvent.Type == BattleEventType.SquadDisengaged
            && battleEvent.Description.Contains("timely enemy interception"));
        Assert.True(escaped.PrimarySquadId == firstRunner.Id
            || escaped.PrimarySquadId == secondRunner.Id);
        Assert.Single(resolver.BattleHistory.Turns.Last().State.AttackerSquads.Values,
            squad => squad.Status == BattleSquadStatus.Active);
        Assert.Null(resolver.BattleHistory.Outcome);
    }

    [Fact]
    public void ProcessNextTurn_MatchedSpeedSternChaseEndsInsteadOfGrindingToTheTurnCap()
    {
        // Regression for the Xibarrus Theta ambush (2026-08-04). Two Marine squads chased one
        // Abominant at 6.001 vs 6.001 for ~997 turns and hit MaxBattleTurns. Both rules that
        // should have ended it — the pursuit planner's cannot-close override and the contact
        // rules' stalled_pursuit break — were gated on "can the pursuer reach melee this turn",
        // which compared the separation to the pursuer's raw move. In a stern chase the
        // separation settles at exactly that distance, so the gate read as a catch permanently
        // one move away and neither rule ever fired.
        //
        // Melee-only so no gun can end the fight instead: there is genuinely nothing this pursuer
        // can ever do, which is precisely when the engagement must be allowed to end.
        SoldierTemplate zeroValueHuman = new(
            73_090,
            TestModelFactory.HumanSpecies,
            "Zero Value Runner",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 0);
        BattleSquad withdrawing = CreateSquad("Withdrawing", 73_091, zeroValueHuman);
        BattleSquad pursuer = CreateSquad(
            "Matched Pursuer",
            73_092,
            TestModelFactory.MarineTemplate,
            isPlayerSquad: true);
        // The Theta speeds exactly: a 0.001 edge, which is inside PursuitSpeedAdvantageTolerance
        // and so is no edge at all — the pursuer needs a thousand turns to gain a single yard.
        ((Soldier)withdrawing.Soldiers[0].Soldier).MoveSpeed = 6f;
        ((Soldier)pursuer.Soldiers[0].Soldier).MoveSpeed = 6.001f;
        // Worth nothing (so its side elects to run), unarmed (so it cannot win the fight it is
        // running from), and durable enough that neither side can settle this by killing. The
        // withdrawal is only declared at the END of turn one, so turn one is fought at engaged
        // ranges — a fragile quarry simply dies there, which is correct behaviour and not the case
        // under test. The chase this is about only begins on turn two, once the quarry is running.
        ((Soldier)withdrawing.Soldiers[0].Soldier).Constitution = 5_000;
        withdrawing.Soldiers[0].ClearWeapons();
        // Melee-only, so no gun can end the engagement instead: there is genuinely nothing this
        // pursuer can ever do to a quarry it cannot catch, which is exactly when the chase has to
        // be allowed to end rather than run to the cap.
        pursuer.Soldiers[0].RangedWeapons.Clear();
        pursuer.Soldiers[0].ClearReadiedRangedWeapons();

        BattleGridManager grid = new();
        // Separation 7 sits just inside the old gate (7 <= move + MeleeContactAllowance = 7.001)
        // and outside the corrected one (net closing 0.001 + allowance = 1.001) — the exact band
        // in which the chase became unkillable.
        Place(grid, withdrawing.Soldiers[0], true, 0, 0);
        Place(grid, pursuer.Soldiers[0], false, 7, 0);
        BattleTurnResolver resolver = CreateResolver(
            grid,
            [withdrawing],
            [pursuer],
            Aggression.Normal,
            Aggression.Aggressive);

        for (int turn = 0; turn < 50 && resolver.BattleHistory.Outcome == null; turn++)
        {
            resolver.ProcessNextTurn();
        }

        BattleOutcome outcome = Assert.IsType<BattleOutcome>(resolver.BattleHistory.Outcome);
        Assert.Equal(BattleEndReason.Withdrawal, outcome.EndReason);
        Assert.Contains(withdrawing.Id, outcome.DisengagedSquadIds);
        // The point of the whole exercise: it ends in a handful of turns rather than grinding.
        Assert.True(
            resolver.BattleHistory.Turns.Count < 10,
            $"an uncatchable quarry should be let go promptly; took "
                + $"{resolver.BattleHistory.Turns.Count} turns");
    }

    [Fact]
    public void ProcessNextTurn_MatchedSpeedPursuerWithAGunResolvesRatherThanChasing()
    {
        // The armed half of the Theta scenario, and the reason the fix is two-part. Ending the
        // chase is not enough on its own: arrival value was measured against the quarry's current
        // position, so running scored the full worth of closing six yards that the quarry
        // immediately reopened, and beat standing and firing every turn. With the withdrawal
        // netted out there is no arrival left to buy and the guns settle it quickly.
        SoldierTemplate zeroValueHuman = new(
            73_100,
            TestModelFactory.HumanSpecies,
            "Zero Value Runner",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 0);
        BattleSquad withdrawing = CreateSquad("Withdrawing", 73_101, zeroValueHuman);
        BattleSquad pursuer = CreateSquad(
            "Armed Matched Pursuer",
            73_102,
            TestModelFactory.MarineTemplate,
            isPlayerSquad: true);
        ((Soldier)withdrawing.Soldiers[0].Soldier).MoveSpeed = 6;
        ((Soldier)pursuer.Soldiers[0].Soldier).MoveSpeed = 6;

        BattleGridManager grid = new();
        Place(grid, withdrawing.Soldiers[0], true, 0, 0);
        Place(grid, pursuer.Soldiers[0], false, 6, 0);
        BattleTurnResolver resolver = CreateResolver(
            grid,
            [withdrawing],
            [pursuer],
            Aggression.Normal,
            Aggression.Aggressive);

        for (int turn = 0; turn < 50 && resolver.BattleHistory.Outcome == null; turn++)
        {
            resolver.ProcessNextTurn();
        }

        BattleOutcome outcome = Assert.IsType<BattleOutcome>(resolver.BattleHistory.Outcome);
        Assert.NotEqual(BattleEndReason.TurnCap, outcome.EndReason);
    }

    // Enormous nominal reach, no ability to hit or hurt anything at it: accuracy and damage are
    // floored and the round degrades with range, so both halves of CalculateOptimalDistance
    // (can I hit here, can I wound here) collapse to nothing.
    private static void EquipLongRangedPeashooter(BattleSoldier soldier)
    {
        RangedWeapon peashooter = new(new RangedWeaponTemplate(
            73_040,
            "Test Long Barrel",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: -20,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 1,
            maxDistance: 2000,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 1,
            doesDamageDegradeWithRange: true,
            reloadTime: 1,
            0,
            0,
            0));
        soldier.RangedWeapons.Clear();
        soldier.ClearReadiedRangedWeapons();
        soldier.RangedWeapons.Add(peashooter);
        soldier.ReadyWeapon(peashooter);
    }

    // Lascannon-shaped: hits hard and does not lose damage over distance, so its killing reach is
    // its full range. Accuracy is pushed well past anything in the rules database on purpose —
    // GetRangeForModifier is exponential (2*e^(-m/2.4663)), so reaching out to 400 yards takes a
    // to-hit total near 24, and the point here is an unambiguous contrast with the peashooter
    // rather than a realistic gun.
    private static void EquipAccurateLongGun(BattleSoldier soldier)
    {
        RangedWeapon longGun = new(new RangedWeaponTemplate(
            73_060,
            "Test Long Gun",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 25,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 40,
            maxDistance: 2000,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 1,
            doesDamageDegradeWithRange: false,
            reloadTime: 1,
            0,
            0,
            0));
        soldier.RangedWeapons.Clear();
        soldier.ClearReadiedRangedWeapons();
        soldier.RangedWeapons.Add(longGun);
        soldier.ReadyWeapon(longGun);
    }

    private static BattleTurnResolver CreateResolver(
        BattleGridManager grid,
        IList<BattleSquad> attackers,
        IList<BattleSquad> defenders,
        Aggression attackerAggression,
        Aggression defenderAggression)
    {
        GameRulesData rules = new();
        Date date = new(1, 1, 1);
        string originalDirectory = Environment.CurrentDirectory;
        try
        {
            Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
            GameDataSingleton.Instance.LoadGameDataFromBlob(rules, date, null);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }

        RNG.Reset(73_000);
        BattleAftermathDependencies aftermath = new(
            date,
            StaticRNG.Instance,
            NoOpPlayerBattleAftermathSink.Instance);
        BattleExecutionContext execution = new(rules, StaticRNG.Instance, aftermath);
        return new BattleTurnResolver(
            grid,
            attackers,
            defenders,
            region: null,
            execution,
            new BattleSideProfile(attackerAggression, BattleRole.Attacker),
            new BattleSideProfile(defenderAggression, BattleRole.Defender));
    }

    private static BattleSquad CreateSquad(
        string name,
        int soldierId,
        SoldierTemplate soldierTemplate,
        bool isPlayerSquad = false)
    {
        Faction faction = CreateFaction(soldierId + 10_000, name, soldierTemplate);
        SquadTemplate squadTemplate = new(
            soldierId,
            $"{name} Template",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(soldierTemplate, 0, 1)],
            SquadTypes.None)
        {
            Faction = faction
        };
        Soldier soldier = TestModelFactory.CreateSoldier(soldierTemplate, $"{name} Soldier");
        soldier.Id = soldierId;
        Squad squad = new(name, null, squadTemplate);
        squad.AddSquadMember(soldier);
        return new BattleSquad(isPlayerSquad, squad);
    }

    private static void Place(
        BattleGridManager grid,
        BattleSoldier soldier,
        bool side,
        int x,
        int y)
    {
        soldier.TopLeft = new ValueTuple<int, int>(x, y);
        grid.PlaceSoldier(soldier, side, [soldier.TopLeft.Value]);
    }

    private static Faction CreateFaction(int id, string name, SoldierTemplate template)
    {
        return new Faction(
            id,
            name,
            Color.Red,
            isPlayerFaction: false,
            isDefaultFaction: false,
            canInfiltrate: false,
            GrowthType.None,
            new Dictionary<int, Species> { [template.Species.Id] = template.Species },
            new Dictionary<int, SoldierTemplate> { [template.Id] = template },
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, Models.Units.UnitTemplate>(),
            new Dictionary<int, Models.Fleets.BoatTemplate>(),
            new Dictionary<int, Models.Fleets.ShipTemplate>(),
            new Dictionary<int, Models.Fleets.FleetTemplate>());
    }

    private sealed class NoOpPlayerBattleAftermathSink : IPlayerBattleAftermathSink
    {
        public static NoOpPlayerBattleAftermathSink Instance { get; } = new();

        public void MoveToFallenBrothers(PlayerSoldier soldier) { }
        public void AddRecoveredGeneseed(float purity) { }
        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) { }
    }
}
