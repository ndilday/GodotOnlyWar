using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Battles;
using OnlyWar.Battles.Actions;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// REPRODUCIBLE BASELINE and focused behavioral coverage for ranged-pursuit scoring. It drives
/// one pursuit geometry per named scenario through the real
/// <see cref="SquadEngagementPolicy"/> option search, captures every candidate's scoring terms
/// alongside the three range quantities and the two movement speeds, and writes the whole table to
/// a trace file.
///
/// <para>The report remains the before/after evidence surface. Focused assertions pin the intended
/// choice, access, and position invariants without freezing every calibration number.</para>
///
/// <para>THE THREE RANGES, as reported per scenario. See §5.5 of the design reference for the
/// proposed distinction and for which of them pursuit scoring reads today.</para>
/// <list type="bullet">
/// <item><c>useful</c> — <see cref="BattleModifiersUtil.CalculateOptimalDistance"/>, the outer edge
/// of the band where the guns still do real work.</item>
/// <item><c>optimal</c> — <see cref="BattleSquadCapabilityProfile.EffectiveEngagementRange"/>, the
/// argmax of the modeled exchange.</item>
/// <item><c>contact</c> — <see cref="BattleContactRules.MeleeContactAllowance"/>, the melee
/// objective.</item>
/// </list>
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class RangedPursuitBaselineTests
{
    private static readonly string TraceDirectory = Path.Combine(
        Path.GetTempPath(),
        "GodotOnlyWar",
        "pursuit-baseline");

    /// <summary>How a baseline pursuer is armed. Drives nothing but the loadout.</summary>
    internal enum PursuerArmament
    {
        /// <summary>A long rifle and a marksman behind it: real removal at real range.</summary>
        EffectiveRifle,
        /// <summary>The same rifle in untrained hands, fired from near its reach.</summary>
        IneffectiveLongRange,
        /// <summary>A 30-yard sidearm: nothing at all until the gap is closed.</summary>
        ShortRangedSidearm,
        /// <summary>A blade and no gun: contact is the only play.</summary>
        MeleeOnly,
        /// <summary>
        /// The same rifle in middling hands. The point of this loadout is the ROOT ACTION: the
        /// snap shot is not worth taking, so a pursuit Hold plans an <c>Aim</c> instead, which is
        /// the only condition under which <see cref="EngagementPotential"/> awards a fire-window
        /// value at all. Without a row like this the whole term reads zero across the matrix and
        /// the baseline would silently fail to cover it.
        /// </summary>
        ModerateRifle
    }

    internal sealed record BaselineScenario(
        string Name,
        PursuerArmament Armament,
        float PursuerSpeed,
        float QuarrySpeed,
        int Separation,
        bool ExpectHoldLegal,
        float WeaponRange = 0,
        int ExistingAimTurns = -1);

    /// <summary>
    /// The baseline matrix. Families 1-4 are the weapon/doctrine cases; 5a-5c hold the loadout and
    /// geometry fixed and vary only the speed ratio, so the speed-driven terms can be read against
    /// an otherwise identical row.
    /// </summary>
    internal static IReadOnlyList<BaselineScenario> Scenarios { get; } =
    [
        new("1-effective-rifle-vs-withdrawing-infantry",
            PursuerArmament.EffectiveRifle, 8f, 3f, 150, ExpectHoldLegal: true),
        // The same effective rifle, moved out past PreferredBandLower so that the contact-progress
        // gradient and (at a narrow speed edge) the access term are both alive AT THE SAME TIME as
        // a real standing shot. This is the Grist Nine shape, and the row the "further closing must
        // justify the shots it costs" rule is really about.
        new("1b-effective-rifle-beyond-band-lower",
            PursuerArmament.EffectiveRifle, 8f, 3f, 900, ExpectHoldLegal: true),
        new("1c-effective-rifle-beyond-band-lower-narrow-speed-edge",
            PursuerArmament.EffectiveRifle, 8f, 7.5f, 900, ExpectHoldLegal: true),
        new("1d-moderate-rifle-aiming-pursuit-hold",
            PursuerArmament.ModerateRifle, 8f, 3f, 60, ExpectHoldLegal: true),
        new("2-ineffective-fire-at-long-range",
            PursuerArmament.IneffectiveLongRange, 8f, 3f, 950, ExpectHoldLegal: true),
        new("3-short-ranged-weapon-must-close",
            PursuerArmament.ShortRangedSidearm, 8f, 3f, 300, ExpectHoldLegal: true),
        new("4-melee-pursuer",
            PursuerArmament.MeleeOnly, 8f, 3f, 200, ExpectHoldLegal: false),
        new("5a-rifle-pursuer-much-faster",
            PursuerArmament.EffectiveRifle, 8f, 3f, 400, ExpectHoldLegal: true),
        new("5b-rifle-pursuer-nearly-equal-speed",
            PursuerArmament.EffectiveRifle, 8f, 7.5f, 400, ExpectHoldLegal: true),
        new("5c-rifle-pursuer-equal-speed",
            PursuerArmament.EffectiveRifle, 8f, 8f, 400, ExpectHoldLegal: true),
        // The sidearm rows are the ones that are genuinely OUTSIDE their desired range, so they
        // are where the access term is alive and where the speed ratio can be read off it. The
        // rifle rows above sit inside theirs and earn no access value at any speed ratio.
        new("5d-sidearm-pursuer-nearly-equal-speed",
            PursuerArmament.ShortRangedSidearm, 8f, 7.5f, 300, ExpectHoldLegal: true),
        new("5e-sidearm-pursuer-equal-speed",
            PursuerArmament.ShortRangedSidearm, 8f, 8f, 300, ExpectHoldLegal: true),
        new("6-immediate-volley-before-withdrawal",
            PursuerArmament.EffectiveRifle, 8f, 8f, 55, true, WeaponRange: 60),
        new("7-preparation-window-closes",
            PursuerArmament.ModerateRifle, 8f, 8f, 55, true, WeaponRange: 60),
        new("8-completed-aim-fires-before-withdrawal",
            PursuerArmament.ModerateRifle, 8f, 8f, 55, true, WeaponRange: 60,
            ExistingAimTurns: 3)
    ];

    [Fact]
    public void PursuitBaseline_ReportsCandidateTermsForEveryScenario()
    {
        List<string> report = [];
        List<BaselineMeasurement> measurements = [];
        foreach (BaselineScenario scenario in Scenarios)
        {
            BaselineMeasurement measurement = Measure(scenario);
            measurements.Add(measurement);
            report.Add(measurement.RenderHeader());
            report.AddRange(measurement.RenderCandidates());
        }

        Directory.CreateDirectory(TraceDirectory);
        string path = Path.Combine(TraceDirectory, "ranged-pursuit-baseline.log");
        File.WriteAllLines(path, report);
        foreach (string line in report) Console.WriteLine(line);
        Console.WriteLine("PURSUIT_BASELINE trace=" + path);

        foreach (BaselineMeasurement measurement in measurements)
        {
            Assert.True(
                measurement.Candidates.Count > 0,
                $"{measurement.Scenario.Name} produced no legal candidate");
            // Legality is a doctrine question, owned by the option mask. Attributing a decision to
            // scoring is only honest once holding is known to have been on the table at all.
            Assert.Equal(
                measurement.Scenario.ExpectHoldLegal,
                measurement.Candidates.Any(
                    candidate => candidate.Kind == EngagementOptionKind.Hold));
            Assert.True(
                measurement.UsefulFiringRange >= 0,
                $"{measurement.Scenario.Name} reported a negative useful firing range");
        }
    }

    /// <summary>
    /// The one decision in the matrix that is masked rather than scored, and therefore safe to
    /// pin: a pursuer with no gun has contact as its only objective, so every legal option closes.
    /// </summary>
    [Fact]
    public void MeleePursuer_HasOnlyClosingOptions()
    {
        BaselineMeasurement measurement = Measure(
            Scenarios.Single(scenario => scenario.Name == "4-melee-pursuer"));

        Assert.All(
            measurement.Candidates,
            candidate => Assert.True(
                IsClosing(candidate.Kind),
                $"a melee pursuer was offered {candidate.Kind}"));
    }

    /// <summary>
    /// A pursuer that cannot out-run its quarry earns no access value, whatever else the score
    /// says: the arrival never happens, so there is no delay to buy out. This is the property
    /// §5.2 records, restated here on the baseline rows so the matrix carries it.
    ///
    /// <para>IT IS ASSERTED ON THE SIDEARM ROWS, NOT THE RIFLE ONES, and that is the correction
    /// this fixture was rewritten for. Access is also zero for a squad already at the range it
    /// wants, because there is no gap left to close, and every rifle row sits inside its desired
    /// range — <c>EffectiveEngagementRange</c> is 711 yards against a separation of 400. Reading
    /// those zeroes as evidence about speed would have credited the speed guard with a result that
    /// the geometry produced on its own. Only the 30-yard sidearm at 300 yards is genuinely
    /// outside its desired range, so only there does the speed ratio decide the term.</para>
    /// </summary>
    [Fact]
    public void EqualAndNearlyEqualClosingSpeeds_HaveFiniteScoresWithoutAnAccessSpike()
    {
        BaselineMeasurement nearlyEqual = Measure(
            Scenarios.Single(
                scenario => scenario.Name == "5d-sidearm-pursuer-nearly-equal-speed"));
        BaselineMeasurement equal = Measure(
            Scenarios.Single(scenario => scenario.Name == "5e-sidearm-pursuer-equal-speed"));

        Assert.All(nearlyEqual.Candidates.Concat(equal.Candidates), candidate =>
        {
            Assert.True(float.IsFinite(candidate.Score));
            Assert.True(float.IsFinite(candidate.AccessPotential));
        });
        float nearlyEqualRun = nearlyEqual.Candidates.Single(candidate =>
            candidate.Kind == EngagementOptionKind.RunToward).Score;
        float equalRun = equal.Candidates.Single(candidate =>
            candidate.Kind == EngagementOptionKind.RunToward).Score;
        Assert.InRange(System.Math.Abs(nearlyEqualRun - equalRun), 0, 0.25f);
    }

    /// <summary>
    /// The contact-progress half of <c>role_term</c> aims at <c>PreferredBandLower</c>, which is
    /// 0.7 x weapon REACH rather than any effectiveness-derived quantity. For a 1000-yard rifle
    /// that is 700 yards, so the term saturates at every separation inside 700 and its candidate
    /// delta is exactly zero there — the whole band in which a pursuit decision is actually made.
    ///
    /// <para>This is recorded rather than asserted as desirable. It contradicts the claim in
    /// Design/Reference/BattleLogic.md §5.2 that the saturating form "has no flat region, so there
    /// is always a gradient toward the quarry however far away it is": the form has no flat region
    /// in the distance, but it does have one inside the desired range, and the desired range here
    /// is large. A later phase is expected to change this; the test then changes with it.</para>
    /// </summary>
    [Fact]
    public void UsefulFirePursuit_BalancesVolleyApproachAndNoDuplicateCredit()
    {
        BaselineMeasurement volley = Measure(
            Scenarios.Single(scenario => scenario.Name == "1d-moderate-rifle-aiming-pursuit-hold"));
        BaselineMeasurement ineffective = Measure(
            Scenarios.Single(scenario => scenario.Name == "2-ineffective-fire-at-long-range"));
        BaselineMeasurement inside = Measure(
            Scenarios.Single(scenario => scenario.Name == "5a-rifle-pursuer-much-faster"));

        Assert.Equal(EngagementOptionKind.Hold, volley.Chosen);
        Assert.True(
            ineffective.Candidates.Single(candidate => candidate.Kind == EngagementOptionKind.RunToward)
                .RoleTerm > 0);
        Assert.All(inside.Candidates, candidate =>
        {
            Assert.Equal(0, candidate.RoleTerm, 4);
            Assert.Equal(0, candidate.AccessPotential, 4);
        });
    }

    [Fact]
    public void ShortRangeAndMeleePursuers_StillClose()
    {
        BaselineMeasurement shortRange = Measure(
            Scenarios.Single(scenario => scenario.Name == "3-short-ranged-weapon-must-close"));
        BaselineMeasurement melee = Measure(
            Scenarios.Single(scenario => scenario.Name == "4-melee-pursuer"));

        Assert.Equal(EngagementOptionKind.RunToward, shortRange.Chosen);
        Assert.Equal(EngagementOptionKind.RunToward, melee.Chosen);
    }

    [Fact]
    public void CurrentVolleyAndCompletedAimFireBeforeTheQuarryMoves()
    {
        BaselineMeasurement volley = Measure(Scenarios.Single(scenario =>
            scenario.Name == "6-immediate-volley-before-withdrawal"));
        BaselineMeasurement completedAim = Measure(Scenarios.Single(scenario =>
            scenario.Name == "8-completed-aim-fires-before-withdrawal"));

        Assert.True(volley.Candidates.Single(candidate =>
            candidate.Kind == EngagementOptionKind.Hold).ImmediateExchange > 0);
        Assert.True(completedAim.Candidates.Single(candidate =>
            candidate.Kind == EngagementOptionKind.Hold).ImmediateExchange > 0);
    }

    [Fact]
    public void PreparationLosesReadinessAndFireWindowWhenQuarryWillLeaveRange()
    {
        BaselineMeasurement closes = Measure(Scenarios.Single(scenario =>
            scenario.Name == "7-preparation-window-closes"));
        CandidateTerms hold = closes.Candidates.Single(candidate =>
            candidate.Kind == EngagementOptionKind.Hold);

        Assert.Equal(0, hold.ImmediateExchange, 4);
        Assert.Equal(0, hold.Readiness, 4);
        Assert.Equal(0, hold.FireWindow, 4);
    }

    [Fact]
    public void HoldAndAdvanceProjectTheSameWithdrawalInterval()
    {
        BaselineMeasurement measurement = Measure(Scenarios.Single(scenario =>
            scenario.Name == "3-short-ranged-weapon-must-close"));
        CandidateTerms hold = measurement.Candidates.Single(candidate =>
            candidate.Kind == EngagementOptionKind.Hold);
        CandidateTerms run = measurement.Candidates.Single(candidate =>
            candidate.Kind == EngagementOptionKind.RunToward);

        Assert.True(run.RoleTerm > hold.RoleTerm,
            $"hold={hold.RoleTerm:F4}, run={run.RoleTerm:F4}");
        Assert.Equal(EngagementOptionKind.RunToward, measurement.Chosen);
    }

    [Fact]
    public void PursuitFireDiagnostics_AreCounterfactualAndDoNotChangeTheDecision()
    {
        Action<string> previous = BattleLog.Sink;
        try
        {
            BattleLog.Sink = null;
            var without = Measure(Scenarios[0]);
            BattleLog.Sink = _ => { };
            SquadEngagementDecision traced = null;
            var with = Measure(Scenarios[0], decision => traced = decision);
            Assert.Equal(without.Chosen, with.Chosen);
            Assert.Equal(without.Candidates, with.Candidates);
            string record = Assert.Single(traced.PursuitDiagnostics);
            Assert.StartsWith("PURSUIT_FIRE_EVAL ", record);
            Assert.Contains("hold_legal=true ", record);
            Assert.Contains("conventional_reason=positive_exchange", record);
            Assert.All(record.Split(' ').Skip(1), field => Assert.Contains("=", field));
            Measure(Scenarios[0], decision => traced = decision, EngagementSquadRole.Press);
            record = Assert.Single(traced.PursuitDiagnostics);
            Assert.Contains("hold_legal=false ", record);
            Assert.Contains("conventional_reason=positive_exchange", record);
            Assert.DoesNotContain(traced.Candidates, candidate => candidate.Kind == EngagementOptionKind.Hold);
        }
        finally { BattleLog.Sink = previous; }
    }

    internal static BaselineMeasurement Measure(BaselineScenario scenario,
        Action<SquadEngagementDecision> inspect = null,
        EngagementSquadRole role = EngagementSquadRole.Pursuit)
    {
        int index = Scenarios.ToList().FindIndex(
            candidate => candidate.Name == scenario.Name);
        int pursuerId = 82_100 + index;
        int quarryId = 82_200 + index;
        float pursuerMelee = scenario.Armament == PursuerArmament.MeleeOnly ? 0.95f : 0.05f;
        BattleSquad pursuer = Squad($"Baseline Pursuer {index}", pursuerId, 20, pursuerMelee);
        BattleSquad quarry = Squad($"Baseline Quarry {index}", quarryId, 10, 0.05f);
        BattleSoldier pursuerSoldier = pursuer.Soldiers[0];
        BattleSoldier quarrySoldier = quarry.Soldiers[0];
        RangedWeapon pursuitWeapon = null;

        switch (scenario.Armament)
        {
            case PursuerArmament.EffectiveRifle:
                ((Soldier)pursuerSoldier.Soldier).Dexterity = 20;
                ((Soldier)pursuerSoldier.Soldier).AddSkillPoints(TestSkills.Ranged, 256);
                pursuitWeapon = EquipRifle(pursuerSoldier, 92_100 + index,
                    range: scenario.WeaponRange > 0 ? scenario.WeaponRange : 1_000,
                    damage: 20);
                break;
            case PursuerArmament.IneffectiveLongRange:
                // Same gun, no marksmanship, and armour on the quarry: the shot is legal and
                // worth almost nothing, which is the case this row exists to capture.
                pursuitWeapon = EquipRifle(pursuerSoldier, 92_100 + index,
                    range: scenario.WeaponRange > 0 ? scenario.WeaponRange : 1_000,
                    damage: 20);
                quarrySoldier.Armor = new Armor(
                    new ArmorTemplate(93_100 + index, "Baseline Flak", 12, 0));
                break;
            case PursuerArmament.ShortRangedSidearm:
                ((Soldier)pursuerSoldier.Soldier).Dexterity = 20;
                ((Soldier)pursuerSoldier.Soldier).AddSkillPoints(TestSkills.Ranged, 256);
                pursuitWeapon = EquipPistol(pursuerSoldier, 92_100 + index,
                    range: scenario.WeaponRange > 0 ? scenario.WeaponRange : 30,
                    damage: 20);
                break;
            case PursuerArmament.MeleeOnly:
                EquipMelee(pursuerSoldier, 92_100 + index);
                break;
            case PursuerArmament.ModerateRifle:
                ((Soldier)pursuerSoldier.Soldier).Dexterity = 14;
                pursuitWeapon = EquipRifle(pursuerSoldier, 92_100 + index,
                    range: scenario.WeaponRange > 0 ? scenario.WeaponRange : 1_000,
                    damage: 20);
                break;
        }
        EquipMelee(quarrySoldier, 92_300 + index);
        ((Soldier)pursuerSoldier.Soldier).MoveSpeed = scenario.PursuerSpeed;
        ((Soldier)quarrySoldier.Soldier).MoveSpeed = scenario.QuarrySpeed;

        BattleGridManager grid = new();
        Place(grid, pursuer, true, 0, 0);
        Place(grid, quarry, false, scenario.Separation, 0);
        if (scenario.ExistingAimTurns >= 0 && pursuitWeapon != null)
        {
            pursuerSoldier.Aim = (quarrySoldier.Soldier.Id, pursuitWeapon,
                scenario.ExistingAimTurns);
        }
        Dictionary<int, EngagementRoleConstraint> constraints = new()
        {
            [pursuer.Id] = new EngagementRoleConstraint(
                role,
                RoleTargets: [quarry]),
            [quarry.Id] = new EngagementRoleConstraint(EngagementSquadRole.Bound)
        };
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([pursuer], [quarry], constraints);
        SquadEngagementFrame frame = paired.Frames[pursuer.Id];
        BattleSquadCapabilityProfile profile = paired.Profiles[pursuer.Id];
        SquadEngagementDecision decision = Planner(grid, pursuer, quarry).ChooseEngagementOption(
            pursuer,
            frame,
            paired.Profiles,
            paired.Frames,
            [quarry],
            [quarry]);
        inspect?.Invoke(decision);

        float usefulFiringRange = System.Math.Max(
            0,
            BattleModifiersUtil.CalculateOptimalDistance(
                pursuerSoldier,
                quarrySoldier.Soldier.Size,
                quarrySoldier.Armor?.Template.ArmorProvided ?? 0,
                quarrySoldier.Soldier.Constitution,
                quarrySoldier.Soldier.Template.Species.RangedEvasion));

        return new BaselineMeasurement(
            scenario,
            profile,
            frame.QuarryRunSpeed,
            usefulFiringRange,
            decision.Chosen.Kind,
            decision.Candidates
                .OrderBy(candidate => candidate.Kind)
                .Select(candidate => new CandidateTerms(
                    candidate.Kind,
                    candidate.Score,
                    candidate.ImmediateEnemyRemoval
                        - candidate.ImmediateFriendlyFire
                        - candidate.IncomingNow
                        + candidate.MeleeValue,
                    candidate.RoleTerm,
                    candidate.AccessPotentialValue,
                    candidate.FutureExchange.Sum() + candidate.ArrivalTimeValue,
                    candidate.ReadinessValue,
                    candidate.FireWindowValue,
                    candidate.ContactCommitmentCost,
                    candidate.FeasibleSpeed))
                .ToList());
    }

    /// <summary>
    /// One scenario's measured row: the geometry and range targets it was run at, plus one
    /// <see cref="CandidateTerms"/> per legal option.
    /// </summary>
    internal sealed record BaselineMeasurement(
        BaselineScenario Scenario,
        BattleSquadCapabilityProfile Profile,
        float QuarryRunSpeed,
        float UsefulFiringRange,
        EngagementOptionKind Chosen,
        IReadOnlyList<CandidateTerms> Candidates)
    {
        internal string RenderHeader() =>
            $"PURSUIT_BASELINE scenario={Scenario.Name} "
            + $"armament={Scenario.Armament} "
            + $"separation={Scenario.Separation} "
            + $"pursuer_speed={Profile.MoveSpeed:F2} "
            + $"quarry_run_speed={QuarryRunSpeed:F2} "
            + $"useful_range={UsefulFiringRange:F2} "
            + $"profile_useful_range={Profile.UsefulFireRange:F2} "
            + $"optimal_range={Profile.EffectiveEngagementRange:F2} "
            + $"contact_range={BattleContactRules.MeleeContactAllowance:F2} "
            + $"band_lower={Profile.PreferredBandLower:F2} "
            + $"band_upper={Profile.PreferredBandUpper:F2} "
            + $"peak_removal_fraction={Profile.PeakRangedRemovalFraction:F5} "
            + $"contact_seeking={Profile.IsContactSeeking} "
            + $"hold_legal={Candidates.Any(c => c.Kind == EngagementOptionKind.Hold)} "
            + $"chosen={Chosen}";

        internal IEnumerable<string> RenderCandidates() => Candidates.Select(candidate =>
            $"  PURSUIT_BASELINE_CANDIDATE scenario={Scenario.Name} "
            + $"kind={candidate.Kind} "
            + $"score={candidate.Score:F4} "
            + $"immediate_exchange={candidate.ImmediateExchange:F4} "
            + $"role_term={candidate.RoleTerm:F4} "
            + $"access_potential={candidate.AccessPotential:F4} "
            + $"net_exchange_potential={candidate.NetExchangePotential:F4} "
            + $"readiness={candidate.Readiness:F4} "
            + $"fire_window={candidate.FireWindow:F4} "
            + $"commitment={candidate.Commitment:F4} "
            + $"feasible_speed={candidate.FeasibleSpeed:F2} "
            + $"chosen={candidate.Kind == Chosen}");
    }

    /// <summary>
    /// The scoring terms §5.2 decomposes a candidate into, named as `ENGAGE_EVAL` names them.
    /// <paramref name="NetExchangePotential"/> folds the discounted projected net-rate value and
    /// the root offset back together, which is the quantity the exchange argument is about.
    /// </summary>
    internal readonly record struct CandidateTerms(
        EngagementOptionKind Kind,
        float Score,
        float ImmediateExchange,
        float RoleTerm,
        float AccessPotential,
        float NetExchangePotential,
        float Readiness,
        float FireWindow,
        float Commitment,
        float FeasibleSpeed);

    private static bool IsClosing(EngagementOptionKind kind) =>
        kind is EngagementOptionKind.StepForward
            or EngagementOptionKind.JogToward
            or EngagementOptionKind.RunToward
            or EngagementOptionKind.CloseToContact;

    private static BattleSquad Squad(
        string name,
        int soldierId,
        int battleValue,
        float meleeFraction)
    {
        SoldierTemplate template = new(
            100_000 + soldierId,
            TestModelFactory.HumanSpecies,
            $"{name} Template",
            1,
            1,
            false,
            0,
            [],
            battleValue: battleValue,
            meleeFraction: meleeFraction);
        Soldier soldier = TestModelFactory.CreateSoldier(template, name);
        soldier.Id = soldierId;
        return new BattleSquad(false, TestModelFactory.CreateSquad(name, soldier));
    }

    private static RangedWeapon EquipRifle(
        BattleSoldier soldier,
        int id,
        float range,
        float damage)
    {
        RangedWeapon weapon = new(new RangedWeaponTemplate(
            id,
            "Baseline Rifle",
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

    private static RangedWeapon EquipPistol(
        BattleSoldier soldier,
        int id,
        float range,
        float damage)
    {
        RangedWeapon weapon = new(new RangedWeaponTemplate(
            id,
            "Baseline Pistol",
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
            "Baseline Blade",
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
        int y)
    {
        BattleSoldier soldier = squad.Soldiers[0];
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
            new SeededRNG(82_000));
    }
}
