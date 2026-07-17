using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Phase 6 of Design/Active/MoraleAndRout.md (§4.3): command auras, the weak-coefficient
/// generalisation of synapse. Covers the CommandAuraEvaluator geometry (living HQ support,
/// non-stacking, the stateless every-HQ-destroyed loss reading), the signed w6 stress term,
/// the never-a-skip rule, and the withdrawal forecast's second aura-loss consumer through
/// the §8.2 command-collapse seam. Pure evaluator/forecast tests with an injected
/// deterministic RNG — no static RNG state is touched, so no shared-state collection is
/// needed.
/// </summary>
public class CommandAuraTests
{
    // The §5.2 reference stress case (mirrors the MoraleConstants hand-calculation): 25%
    // casualties this turn, 25% cumulative, force losing -> shock 0.75, stress ~1.06.
    private const float LosingForceDisadvantage = 0.41f;

    /// <summary>Zero-noise RNG: every z-draw is 0, so fails are purely stress vs resolve.</summary>
    private sealed class ZeroNoiseRNG : IRNG
    {
        public double GetDoubleInRange(double lowerBound, double upperBound) => lowerBound;
        public double GetLinearDouble() => 0.0;
        public int GetIntBelowMax(int min, int max) => min;
        public double NextRandomZValue() => 0.0;
    }

    private static BattleSquad CreateSquad(
        string name,
        int templateId,
        SquadTypes squadType,
        float ego,
        int soldierCount,
        SoldierTemplate soldierTemplate = null)
    {
        soldierTemplate ??= TestModelFactory.MarineTemplate;
        SquadTemplate squadTemplate = new(
            templateId,
            $"{name} Template",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(soldierTemplate, 0, (byte)soldierCount)],
            squadType);
        Squad squad = new(templateId, name, null, squadTemplate);
        for (int i = 0; i < soldierCount; i++)
        {
            Soldier soldier = TestModelFactory.CreateSoldier(
                template: soldierTemplate, name: $"{name} {i + 1}");
            soldier.Id = (templateId * 100) + i;
            soldier.Ego = ego;
            squad.AddSquadMember(soldier);
        }
        return new BattleSquad(false, squad);
    }

    private static void PlaceSquad(BattleGridManager grid, BattleSquad squad, int x, int y)
    {
        for (int i = 0; i < squad.Soldiers.Count; i++)
        {
            BattleSoldier soldier = squad.Soldiers[i];
            Tuple<int, int> cell = new(x + i, y);
            grid.PlaceSoldier(soldier, true, new List<Tuple<int, int>> { cell });
            soldier.TopLeft = cell;
        }
    }

    private static void Destroy(BattleSquad squad)
    {
        foreach (BattleSoldier soldier in squad.Soldiers.ToList())
        {
            squad.RemoveSoldier(soldier);
        }
    }

    // --- CommandAuraEvaluator geometry ---

    [Fact]
    public void SquadWithinLivingHqRadius_GetsSupport_BeyondRadiusGetsNothing()
    {
        BattleGridManager grid = new();
        BattleSquad hq = CreateSquad("Captain", 81_001, SquadTypes.HQ, ego: 14f, soldierCount: 1);
        BattleSquad near = CreateSquad("Near Troops", 81_002, SquadTypes.None, ego: 10f, soldierCount: 5);
        BattleSquad far = CreateSquad("Far Troops", 81_003, SquadTypes.None, ego: 10f, soldierCount: 5);
        PlaceSquad(grid, hq, 0, 0);
        PlaceSquad(grid, near, 50, 0);
        PlaceSquad(grid, far, (int)MoraleConstants.CommandAuraRadius + 100, 0);
        BattleSquad[] roster = [hq, near, far];

        Assert.Equal(
            MoraleConstants.CommandAuraSupportStrength,
            CommandAuraEvaluator.ComputeCommandAuraModifier(near, roster, grid));
        // A living commander who is merely out of range is no aura — and no death shock.
        Assert.Equal(0f, CommandAuraEvaluator.ComputeCommandAuraModifier(far, roster, grid));
    }

    [Fact]
    public void Support_DoesNotStackAcrossMultipleHqs()
    {
        BattleGridManager grid = new();
        BattleSquad hqA = CreateSquad("Captain", 81_011, SquadTypes.HQ, ego: 14f, soldierCount: 1);
        BattleSquad hqB = CreateSquad("Chaplain", 81_012, SquadTypes.HQ, ego: 14f, soldierCount: 1);
        BattleSquad troops = CreateSquad("Troops", 81_013, SquadTypes.None, ego: 10f, soldierCount: 5);
        PlaceSquad(grid, hqA, 0, 0);
        PlaceSquad(grid, hqB, 0, 10);
        PlaceSquad(grid, troops, 20, 0);

        // Two HQs in radius still supply exactly one CommandAuraSupportStrength (max, not sum).
        Assert.Equal(
            MoraleConstants.CommandAuraSupportStrength,
            CommandAuraEvaluator.ComputeCommandAuraModifier(troops, [hqA, hqB, troops], grid));
    }

    [Fact]
    public void EveryHqDestroyed_AppliesLossTerm_SurvivingHqAnywherePreventsIt()
    {
        BattleGridManager grid = new();
        BattleSquad deadHq = CreateSquad("Dead Captain", 81_021, SquadTypes.HQ, ego: 14f, soldierCount: 1);
        BattleSquad farHq = CreateSquad("Far Captain", 81_022, SquadTypes.HQ, ego: 14f, soldierCount: 1);
        BattleSquad troops = CreateSquad("Troops", 81_023, SquadTypes.None, ego: 10f, soldierCount: 5);
        PlaceSquad(grid, deadHq, 0, 0);
        PlaceSquad(grid, farHq, (int)MoraleConstants.CommandAuraRadius + 100, 0);
        PlaceSquad(grid, troops, 20, 0);
        Destroy(deadHq);

        // Another HQ survives (even far out of range): command is not gone, no loss term.
        Assert.Equal(
            0f,
            CommandAuraEvaluator.ComputeCommandAuraModifier(troops, [deadHq, farHq, troops], grid));

        // Every fielded HQ destroyed: the stateless loss reading kicks in for the side.
        Destroy(farHq);
        Assert.Equal(
            -MoraleConstants.CommandLossStress,
            CommandAuraEvaluator.ComputeCommandAuraModifier(troops, [deadHq, farHq, troops], grid));
    }

    [Fact]
    public void SideThatNeverFieldedAnHq_TakesNeitherSupportNorLoss()
    {
        BattleGridManager grid = new();
        BattleSquad troopsA = CreateSquad("Troops A", 81_031, SquadTypes.None, ego: 10f, soldierCount: 5);
        BattleSquad troopsB = CreateSquad("Troops B", 81_032, SquadTypes.None, ego: 10f, soldierCount: 5);
        PlaceSquad(grid, troopsA, 0, 0);
        PlaceSquad(grid, troopsB, 10, 0);

        Assert.Equal(
            0f, CommandAuraEvaluator.ComputeCommandAuraModifier(troopsA, [troopsA, troopsB], grid));
    }

    [Fact]
    public void HqDoesNotSourceItsOwnAura()
    {
        BattleGridManager grid = new();
        BattleSquad hq = CreateSquad("Captain", 81_041, SquadTypes.HQ, ego: 14f, soldierCount: 1);
        PlaceSquad(grid, hq, 0, 0);

        Assert.Equal(0f, CommandAuraEvaluator.ComputeCommandAuraModifier(hq, [hq], grid));
    }

    // --- signed w6 stress term (§5.2): support lowers stress, loss raises it ---

    [Fact]
    public void AuraSupportReducesStress_AndCommandLossRaisesIt()
    {
        List<BattleMoraleEvaluator.SoldierMoraleInput> gaunts = Enumerable.Range(0, 8)
            .Select(i => new BattleMoraleEvaluator.SoldierMoraleInput(i + 1, 8f, false))
            .ToList();
        BattleMoraleEvaluator.MoraleCheckInput Input(float commandAura) => new(
            gaunts,
            CasualtyFractionThisTurn: 0.25f,
            CumulativeCasualtyFraction: 0.25f,
            LeaderDead: false,
            RoutingVisibleFriendlyFraction: 0f,
            LocalOutnumberRatio: 0f,
            CommandAuraSupport: commandAura,
            ForceDisadvantage: LosingForceDisadvantage);

        BattleMoraleEvaluator.MoraleCheckResult unsupported =
            BattleMoraleEvaluator.Evaluate(Input(0f), new ZeroNoiseRNG());
        BattleMoraleEvaluator.MoraleCheckResult supported = BattleMoraleEvaluator.Evaluate(
            Input(MoraleConstants.CommandAuraSupportStrength), new ZeroNoiseRNG());
        BattleMoraleEvaluator.MoraleCheckResult commandLost = BattleMoraleEvaluator.Evaluate(
            Input(-MoraleConstants.CommandLossStress), new ZeroNoiseRNG());

        // Reference case: stress 1.06 > resolve(8) 0.95 -> routs without an aura...
        Assert.Equal(MoraleState.Routing, unsupported.Outcome);
        // ...support strips w6 * strength = 0.25 of shock (stress 0.705) -> holds Steady...
        Assert.True(supported.Stress < unsupported.Stress);
        Assert.Equal(MoraleState.Steady, supported.Outcome);
        // ...and the commander's death supplies a POSITIVE term (§4.3): stress 1.41.
        Assert.True(commandLost.Stress > unsupported.Stress);
        Assert.Equal(MoraleState.Routing, commandLost.Outcome);

        // The closed-form estimator the forecast consumes reads the same signed term.
        Assert.Equal(MoraleState.Routing, BattleMoraleEvaluator.EstimateOutcome(Input(0f)));
        Assert.Equal(
            MoraleState.Steady,
            BattleMoraleEvaluator.EstimateOutcome(Input(MoraleConstants.CommandAuraSupportStrength)));
        Assert.Equal(
            MoraleState.Routing,
            BattleMoraleEvaluator.EstimateOutcome(Input(-MoraleConstants.CommandLossStress)));
    }

    // --- command aura NEVER skips the check; synapse-covered squads are unaffected ---

    [Fact]
    public void CommandAura_NeverSkipsTheMoraleCheck()
    {
        BattleGridManager grid = new();
        BattleSquad hq = CreateSquad("Captain", 81_051, SquadTypes.HQ, ego: 14f, soldierCount: 1);
        BattleSquad troops = CreateSquad("Troops", 81_052, SquadTypes.None, ego: 10f, soldierCount: 5);
        PlaceSquad(grid, hq, 0, 0);
        PlaceSquad(grid, troops, 10, 0);

        // Fully inside the command aura, yet the squad still checks — the skip is synapse-only
        // (§4.3); the aura only feeds the -w6 term.
        Assert.Equal(
            BattleMoraleEvaluator.MoraleSkipReason.Check,
            BattleMoraleEvaluator.ShouldCheckMorale(troops, new[] { hq, troops }, grid));
    }

    [Fact]
    public void SynapseCoveredSquad_StillSkips_RegardlessOfCommandAuraState()
    {
        BattleGridManager grid = new();
        BattleSquad provider = CreateSquad(
            "Synapse", 81_061, SquadTypes.None, ego: 20f, soldierCount: 1,
            TestModelFactory.SynapseProviderTemplate);
        BattleSquad gaunts = CreateSquad("Gaunts", 81_062, SquadTypes.None, ego: 8f, soldierCount: 3);
        BattleSquad deadHq = CreateSquad("Dead HQ", 81_063, SquadTypes.HQ, ego: 14f, soldierCount: 1);
        PlaceSquad(grid, provider, 0, 0);
        PlaceSquad(grid, gaunts, 5, 0);
        PlaceSquad(grid, deadHq, 20, 0);
        Destroy(deadHq);

        // Every HQ is destroyed, but the synapse-covered squad never reaches the stress
        // function at all — the loss term cannot touch it while coverage holds.
        Assert.Equal(
            BattleMoraleEvaluator.MoraleSkipReason.SynapseCovered,
            BattleMoraleEvaluator.ShouldCheckMorale(gaunts, new[] { provider, gaunts, deadHq }, grid));
    }

    // --- §8.2 second consumer: command collapse in the withdrawal forecast ---

    private static WithdrawalForecast.SquadGeometry Hq(int id) =>
        new(id, 1, 10, 4, 8, ProvidesCommandAura: true);

    private static WithdrawalForecast.SquadGeometry Platoon(int id, bool routsIfCommandLost) =>
        new(id, 10, 60, 4, 8, DependsOnCommandAura: true, RoutsIfCommandLost: routsIfCommandLost);

    [Fact]
    public void HqHeldAsRearGuard_SeversItsDependents_OnlyWhenCollapseIsModelled()
    {
        var geometry = new[] { Hq(1), Platoon(2, routsIfCommandLost: true) };

        WithdrawalForecast.Projection modelled = WithdrawalForecast.ProjectOpenGround(
            geometry, fastestPursuerSpeed: 10, oneTurnAttackReach: 3,
            rearGuardSquadId: 1, rearGuardDelayTurns: 1, modelCommandCollapse: true);
        WithdrawalForecast.Projection blind = WithdrawalForecast.ProjectOpenGround(
            geometry, fastestPursuerSpeed: 10, oneTurnAttackReach: 3,
            rearGuardSquadId: 1, rearGuardDelayTurns: 1, modelCommandCollapse: false);

        // Blind: the platoon departs masked behind its Captain. Modelled: his loss routs it,
        // it forfeits the delay, and the pursuit's higher reach runs it down.
        Assert.Equal(10, blind.ExpectedAbleSurvivors);
        Assert.Equal(60, blind.ExpectedSurvivingBattleValue);
        Assert.Equal(1, blind.ExpectedMainBodyMaskedDepartures);
        Assert.Equal(0, modelled.ExpectedAbleSurvivors);
        Assert.Equal(0, modelled.ExpectedSurvivingBattleValue);
        Assert.Equal(2, modelled.ExpectedSquadsIntercepted);
    }

    [Fact]
    public void HqRunDownInTheOpen_AlsoSeversItsDependents()
    {
        // The HQ is not the rear guard — it is simply too close and too slow, and the pursuit
        // catches it. Its dependents lose the aura in that branch all the same. The platoon
        // escapes ordinary reach (final separation 4.5 > 3) but not the routed reach (4.5).
        var geometry = new[]
        {
            new WithdrawalForecast.SquadGeometry(1, 1, 10, 0, 1, ProvidesCommandAura: true),
            new WithdrawalForecast.SquadGeometry(
                2, 10, 60, 6, 8, DependsOnCommandAura: true, RoutsIfCommandLost: true)
        };

        WithdrawalForecast.Projection modelled = WithdrawalForecast.ProjectOpenGround(
            geometry, fastestPursuerSpeed: 8.5f, oneTurnAttackReach: 3, modelCommandCollapse: true);
        WithdrawalForecast.Projection blind = WithdrawalForecast.ProjectOpenGround(
            geometry, fastestPursuerSpeed: 8.5f, oneTurnAttackReach: 3, modelCommandCollapse: false);

        Assert.Equal(10, blind.ExpectedAbleSurvivors);
        Assert.Equal(0, modelled.ExpectedAbleSurvivors);
        Assert.Equal(2, modelled.ExpectedSquadsIntercepted);
    }

    [Fact]
    public void CommandCollapse_IsCappedAtOneLevel()
    {
        // The Captain held as rear guard severs BOTH platoons. Platoon 2's closed-form verdict
        // is that it routs; platoon 3 holds (RoutsIfCommandLost=false). A second-order cascade
        // would let 2's rout drag 3 down as well; the §8.2 one-level cap forbids it, so 3 keeps
        // its own verdict and departs masked behind the delay.
        var geometry = new[]
        {
            Hq(1),
            Platoon(2, routsIfCommandLost: true),
            Platoon(3, routsIfCommandLost: false)
        };

        WithdrawalForecast.Projection projection = WithdrawalForecast.ProjectOpenGround(
            geometry, fastestPursuerSpeed: 10, oneTurnAttackReach: 3,
            rearGuardSquadId: 1, rearGuardDelayTurns: 1, modelCommandCollapse: true);

        Assert.Equal(10, projection.ExpectedAbleSurvivors);
        Assert.Equal(60, projection.ExpectedSurvivingBattleValue);
        Assert.Equal(2, projection.ExpectedSquadsIntercepted);
        Assert.Equal(1, projection.ExpectedMainBodyMaskedDepartures);
    }

    [Fact]
    public void Keystone_CaptainIsProtected_OnlyWhenCommandCollapseIsModelled()
    {
        // §4.3: the Imperial BV spread is nearly flat, so a Captain (10 BV here) never outvalues
        // a squad — he must be protected because his death costs the platoon. Force: Captain HQ,
        // a marine squad (Ego 14, the only other eligible rear guard), and two PDF platoons
        // steadied by the Captain's aura that rout if he is lost.
        var geometry = new[]
        {
            Hq(1),
            new WithdrawalForecast.SquadGeometry(2, 5, 45, 4, 8),
            Platoon(3, routsIfCommandLost: true),
            Platoon(4, routsIfCommandLost: true)
        };

        int? Decide(bool modelCommandCollapse)
        {
            WithdrawalForecast.Projection baseline = WithdrawalForecast.ProjectOpenGround(
                geometry, 10, 3, modelCommandCollapse: modelCommandCollapse);
            WithdrawalForecast.Candidate Candidate(int id) => new(
                id, true, false, true, false, 3, 4, 5,
                WithdrawalForecast.ProjectOpenGround(
                    geometry, 10, 3, rearGuardSquadId: id, rearGuardDelayTurns: 1,
                    modelCommandCollapse: modelCommandCollapse),
                SquadEgo: 14f);
            // The Ego-10 platoons are not candidates — they would be rejected will_break_if_held.
            return WithdrawalForecast.Evaluate(new WithdrawalForecast.Input(
                7, true, baseline, new[] { Candidate(1), Candidate(2) })).SelectedSquadId;
        }

        // Blind to collapse the cheap Captain looks like the perfect sacrifice (everyone else's
        // 165 BV escapes behind him)...
        Assert.Equal(1, Decide(modelCommandCollapse: false));
        // ...but modelling it, his death routs both platoons mid-escape: holding the marine
        // squad instead saves Captain + platoons (130 BV vs 45). The Captain is protected.
        Assert.Equal(2, Decide(modelCommandCollapse: true));
    }
}
