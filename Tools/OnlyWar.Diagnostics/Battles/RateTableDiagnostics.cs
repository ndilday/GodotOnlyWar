using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using OnlyWar.Battles.Actions;
using OnlyWar.Battles.Models;
using OnlyWar.Battles.Placers;
using OnlyWar.Domain;
using OnlyWar.Domain.Equippables;
using OnlyWar.Generation.World;
using OnlyWar.Persistence.Database.GameRules;

using Xunit;

namespace OnlyWar.Diagnostics.Battles;

/// <summary>
/// Opt-in: the engagement planner's squad-pair removal-rate table, broken into its factors, for two
/// squads deployed as the duel tool deploys them. The table is what the exchange forecast reads
/// (Design/Reference/BattleLogic.md §5.2 and §5.3), so
/// comparing its factors with a duel's realized hit rate (squad-duel.txt) and with the hardness
/// tool's hits to disable shows which factor is off.
///
/// For each shooter's term against its chosen target: the un-aimed hit chance of the first shot,
/// the fully aimed one, the shot count and recoil, the take-out chance per hit, the wound progress
/// per hit, the combined removal fraction per hit (take-out + lambda x progress), the per-turn
/// removal un-aimed and sustained (aim cycle), and the target's battle value. Then squad sums, and
/// the implied disables per turn.
///
/// Gated by RUN_RATE_TABLE. Settings: RATE_TABLE_SCENARIO (duel syntax, for example
/// "26 vs 4 @ 150"), RATE_TABLE_SEED (default 1, the duel's first seed), RATE_TABLE_OUT (default
/// %TEMP%\GodotOnlyWar\rate-table.txt). No battle is fought.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class RateTableDiagnostics
{
    [Fact]
    public void RateTable_ReportsItsFactors()
    {
        string scenario = Environment.GetEnvironmentVariable("RATE_TABLE_SCENARIO");
        if (Environment.GetEnvironmentVariable("RUN_RATE_TABLE") == null
            || string.IsNullOrWhiteSpace(scenario))
        {
            return;
        }

        int seed = SquadDuelDiagnostics.ReadInt("RATE_TABLE_SEED", 1);
        string output = Environment.GetEnvironmentVariable("RATE_TABLE_OUT")
            ?? Path.Combine(Path.GetTempPath(), "GodotOnlyWar", "rate-table.txt");
        string[] atSplit = scenario.Split('@', StringSplitOptions.TrimEntries);
        int range = int.Parse(atSplit[1], CultureInfo.InvariantCulture);
        string[] sides = atSplit[0].Split(" vs ", StringSplitOptions.TrimEntries);

        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        // Built and placed exactly as SquadDuelDiagnostics.RunBattle does for A attacking.
        OnlyWar.Abstractions.IEntityIdAllocator ids =
            new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
        SeededRNG buildRandom = new(80_000 + seed);
        List<BattleSquad> sideA = SquadDuelDiagnostics.BuildSquads(
            blob, SquadDuelDiagnostics.ParseSide(sides[0]), ids, "A", buildRandom);
        List<BattleSquad> sideB = SquadDuelDiagnostics.BuildSquads(
            blob, SquadDuelDiagnostics.ParseSide(sides[1]), ids, "B", buildRandom);
        BattleGridManager grid = new();
        new AnnihilationPlacer(grid, checked((ushort)range)).PlaceSquads(sideA, sideB);
        // No coordinator runs here, so assign the tactical values the planner reads in a battle.
        TacticalValueTable.EnsureAssigned(sideA, sideB);

        Dictionary<int, BattleSoldier> soldiers = sideA.Concat(sideB)
            .SelectMany(squad => squad.Soldiers)
            .ToDictionary(soldier => soldier.Soldier.Id);
        Dictionary<int, MeleeWeaponTemplate> melee = soldiers.Values
            .SelectMany(soldier => soldier.MeleeWeapons
                .Select(weapon => weapon.Template)
                .Append(soldier.Soldier.Template.Species.DefaultUnarmedWeapon))
            .GroupBy(template => template.Id)
            .ToDictionary(group => group.Key, group => group.First());
        BattleSquadPlanner planner = new(
            grid, soldiers, new List<IAction>(), new List<IAction>(), new List<IAction>(),
            null, melee, new SeededRNG(76_000));

        StringBuilder report = new();
        report.AppendLine($"Rate table for '{scenario}', seed {seed}. Location fills combine with "
            + $"q = {RemovalMath.LocationFillCombineExponent}. Rates are target battle value per turn at "
            + "the placement range; hits/kill = 1 / (removal per hit).");
        foreach ((List<BattleSquad> shooters, List<BattleSquad> targets) in
            new[] { (sideA, sideB), (sideB, sideA) })
        {
            foreach (BattleSquad squad in shooters)
            {
                Report(report, planner, squad, targets);
            }
        }
        foreach ((List<BattleSquad> shooters, List<BattleSquad> targets) in
            new[] { (sideA, sideB), (sideB, sideA) })
        {
            BattleEngagementFrameBuilder.PairedFrame paired =
                BattleEngagementFrameBuilder.Build(shooters, targets);
            foreach (BattleSquad squad in shooters)
            {
                ReportHoldReadiness(report, planner, grid, soldiers, squad, shooters, targets, paired);
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, report.ToString());
    }

    /// <summary>
    /// Opt-in: the engagement potential's root value (Phi) of each side-A squad, and its chosen
    /// option, as the opening range is swept -- the tool for "does the forecast jump". The squads
    /// are built and placed fresh at every range, exactly as the rate table builds them. Set
    /// RUN_PHI_SWEEP=1, RATE_TABLE_SCENARIO (duel syntax; its range is ignored) and PHI_SWEEP
    /// ("from:to:step" in yards); RATE_TABLE_SEED and RATE_TABLE_OUT as above (default output
    /// %TEMP%\GodotOnlyWar\phi-sweep.txt).
    /// </summary>
    [Fact]
    public void PhiSweep_ReportsTheRootPotentialAcrossOpeningRanges()
    {
        string scenario = Environment.GetEnvironmentVariable("RATE_TABLE_SCENARIO");
        string sweep = Environment.GetEnvironmentVariable("PHI_SWEEP");
        if (Environment.GetEnvironmentVariable("RUN_PHI_SWEEP") == null
            || string.IsNullOrWhiteSpace(scenario)
            || string.IsNullOrWhiteSpace(sweep))
        {
            return;
        }
        int seed = SquadDuelDiagnostics.ReadInt("RATE_TABLE_SEED", 1);
        string output = Environment.GetEnvironmentVariable("RATE_TABLE_OUT")
            ?? Path.Combine(Path.GetTempPath(), "GodotOnlyWar", "phi-sweep.txt");
        string[] sides = scenario.Split('@', StringSplitOptions.TrimEntries)[0]
            .Split(" vs ", StringSplitOptions.TrimEntries);
        float[] bounds = sweep.Split(':').Select(part => float.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        StringBuilder report = new();
        // PHI_LEVEL_BREAKS=1: the experiment switch EngagementPotential.UseLevelBreaks for this sweep.
        bool levelBreaks = Environment.GetEnvironmentVariable("PHI_LEVEL_BREAKS") == "1";
        EngagementPotential.UseLevelBreaks = levelBreaks;
        // PHI_LUCK: the luck spread of the potential's forecasts for this sweep (EngagementPotential.LuckSpread).
        float defaultLuck = EngagementPotential.LuckSpread;
        if (float.TryParse(Environment.GetEnvironmentVariable("PHI_LUCK"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out float luck))
        {
            EngagementPotential.LuckSpread = luck;
        }
        report.AppendLine($"Phi sweep for '{sides[0]} vs {sides[1]}', seed {seed}"
            + $"{(levelBreaks ? ", level breaks" : "")}, luck spread {EngagementPotential.LuckSpread:0.##}: "
            + "range, then per side-A squad rootPhi/chosen.");
        for (float range = bounds[0]; range <= bounds[1] + 0.001f; range += bounds[2])
        {
            OnlyWar.Abstractions.IEntityIdAllocator ids =
                new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
            SeededRNG buildRandom = new(80_000 + seed);
            List<BattleSquad> sideA = SquadDuelDiagnostics.BuildSquads(
                blob, SquadDuelDiagnostics.ParseSide(sides[0]), ids, "A", buildRandom);
            List<BattleSquad> sideB = SquadDuelDiagnostics.BuildSquads(
                blob, SquadDuelDiagnostics.ParseSide(sides[1]), ids, "B", buildRandom);
            BattleGridManager grid = new();
            new AnnihilationPlacer(grid, checked((ushort)range)).PlaceSquads(sideA, sideB);
            TacticalValueTable.EnsureAssigned(sideA, sideB);
            Dictionary<int, BattleSoldier> soldiers = sideA.Concat(sideB)
                .SelectMany(squad => squad.Soldiers)
                .ToDictionary(soldier => soldier.Soldier.Id);
            Dictionary<int, MeleeWeaponTemplate> melee = soldiers.Values
                .SelectMany(soldier => soldier.MeleeWeapons
                    .Select(weapon => weapon.Template)
                    .Append(soldier.Soldier.Template.Species.DefaultUnarmedWeapon))
                .GroupBy(template => template.Id)
                .ToDictionary(group => group.Key, group => group.First());
            BattleSquadPlanner planner = new(
                grid, soldiers, new List<IAction>(), new List<IAction>(), new List<IAction>(),
                null, melee, new SeededRNG(76_000));
            BattleEngagementFrameBuilder.PairedFrame paired =
                BattleEngagementFrameBuilder.Build(sideA, sideB);
            StringBuilder line = new(string.Create(CultureInfo.InvariantCulture, $"  {range,5:F0}"));
            foreach (BattleSquad squad in sideA)
            {
                SquadEngagementDecision decision = planner.ChooseEngagementOption(
                    squad, paired.Frames[squad.Id], paired.Profiles, paired.Frames, sideB);
                float rootPhi = -decision.Chosen.ArrivalTimeValue;
                line.Append(string.Create(CultureInfo.InvariantCulture,
                    $"  {rootPhi,9:F2} {decision.Chosen.Kind,-14}"));
            }
            report.AppendLine(line.ToString());
        }
        EngagementPotential.UseLevelBreaks = false;
        EngagementPotential.LuckSpread = defaultLuck;
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, report.ToString());
    }

    /// <summary>
    /// The two prices of one standing turn, shooter by shooter: the table's sustained rate (what the
    /// exchange forecast credits a standing turn) against the readiness the Hold option's Aim
    /// carries (what the option's immediate term credits the same turn). Between them: the planner's
    /// own aimed shots at the Aim's target, gross (Score per turn of the aim-then-fire cycle) and net
    /// of the ammunition charge (RangedTargetSelector.EvaluateFireTiming, replicated), so each factor
    /// of the gap shows separately.
    /// </summary>
    private static void ReportHoldReadiness(
        StringBuilder report,
        BattleSquadPlanner planner,
        BattleGridManager grid,
        IReadOnlyDictionary<int, BattleSoldier> soldiers,
        BattleSquad squad,
        List<BattleSquad> friendly,
        List<BattleSquad> targets,
        BattleEngagementFrameBuilder.PairedFrame paired)
    {
        // The root state's Stay and Close forecasts, turn by turn (EngagementPotential's trace).
        List<string> forecast = [];
        EngagementPotential.ForecastTraceSquadId = squad.Id;
        EngagementPotential.ForecastTraceSink = forecast.Add;
        SquadEngagementDecision decision;
        try
        {
            decision = planner.ChooseEngagementOption(
                squad, paired.Frames[squad.Id], paired.Profiles, paired.Frames, targets);
        }
        finally
        {
            EngagementPotential.ForecastTraceSink = null;
        }
        EngagementOptionEvaluation hold = decision.Candidates.FirstOrDefault(
            candidate => candidate.Kind == EngagementOptionKind.Hold);
        report.AppendLine();
        report.AppendLine($"== Options of {squad.Name}: chose {decision.Chosen.Kind} ==");
        foreach (EngagementOptionEvaluation candidate in decision.Candidates.OrderByDescending(c => c.Score))
        {
            report.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {candidate.Kind,-15} score={candidate.Score,8:F2} out={candidate.ImmediateEnemyRemoval,6:F2} "
                + $"incoming={candidate.IncomingNow,6:F2} readiness={candidate.ReadinessValue,6:F2} "
                + $"future=[{string.Join(",", candidate.FutureExchange.Select(v => v.ToString("F2", CultureInfo.InvariantCulture)))}] "
                + $"rootPhi={-candidate.ArrivalTimeValue,8:F2} role={candidate.RoleTerm,6:F2} "
                + $"rootCloseAdvantage={candidate.RootCloseAdvantage,7:F2}"));
        }
        report.AppendLine("  root forecast (first evaluation of the root state):");
        // The root is evaluated more than once per decision; the first Stay/Close pair is enough.
        int printed = 0;
        foreach (string line in forecast)
        {
            report.AppendLine(line);
            if (line.Contains("close TOTAL") && ++printed >= 1)
            {
                break;
            }
        }
        BattleSquadCapabilityProfile profile = paired.Profiles[squad.Id];
        report.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"  profile: effectiveEngagementRange={profile.EffectiveEngagementRange:F1} usefulFire={profile.UsefulFireRange:F1} "
            + $"band=[{profile.PreferredBandLower:F1}, {profile.PreferredBandUpper:F1}] contactSeeking={profile.IsContactSeeking}"));
        var curve = BattleEngagementFrameBuilder.DescribeEngagementCurve(
            squad, targets, profile.PreferredBandUpper,
            new float[] { 1, 10, 25, 50, 100, 150, 200, 260, 400, 600, 823 },
            friendly);
        if (curve is { } described)
        {
            report.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  effective-range curve (argmax of out - in - melee over [0, {described.Band:F0}]):"));
            foreach ((float range, float outgoing, float incoming, float melee) in described.Samples)
            {
                report.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"    r={range,4:F0}  out={outgoing,7:F3}  in={incoming,7:F3}  melee={melee,7:F3}  net={outgoing - incoming - melee,8:F3}"));
            }
        }
        report.AppendLine($"== Hold readiness of {squad.Name}: chose {decision.Chosen.Kind} ==");
        if (hold == null)
        {
            report.AppendLine("  (Hold not offered)");
            return;
        }
        Dictionary<int, PairRemovalTerm> terms = planner.GetPairRemovalRates(squad).Values
            .SelectMany(rate => rate.Terms)
            .GroupBy(term => term.ShooterId)
            .ToDictionary(group => group.Key, group => group.First());
        report.AppendLine("  shooter  action  target  tableTarget  range  scarcity  tableSust  grossBest  grossJ  netBest  netJ  readiness  aimShots");
        float sumTable = 0, sumGross = 0, sumNet = 0, sumReadiness = 0, sumGrossAtNet = 0;
        int counted = 0;
        foreach (PlannedSoldierAction action in hold.RootActions ?? [])
        {
            terms.TryGetValue(action.SoldierId, out PairRemovalTerm term);
            float table = term?.SustainedRateAt(term.ReferenceRange) ?? 0;
            sumTable += table;
            if (action.Kind != PlannedSoldierActionKind.Aim
                || !soldiers.TryGetValue(action.SoldierId, out BattleSoldier shooter)
                || action.TargetId is not int targetId
                || !soldiers.TryGetValue(targetId, out BattleSoldier target))
            {
                report.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {action.SoldierId,7}  {action.Kind,-6}  table {table:F3}"));
                continue;
            }
            RangedWeapon weapon = shooter.EquippedRangedWeapons
                .First(candidate => candidate.Template.Id == action.WeaponTemplateId);
            float range = grid.GetDistanceBetweenSoldiers(shooter.Soldier.Id, target.Soldier.Id);
            // As a standing shooter's firing decision prices them (RangedTargetSelector
            // .StandingRoundPrice): one round price for every plan, set by the shooter's share of
            // the work against the rounds he carries, and every plan's burst chosen at that price.
            float price = planner.StandingRoundPrice(shooter, target, weapon, range, out float scarcity);
            float work = planner.WorkPerShooter(shooter, weapon);
            float[] modifiers = Enumerable.Range(0, RangedTargetSelector.FullAimBonusTurns + 1)
                .Select(bonus => RangedTargetSelector.AimAccuracy(shooter, weapon.Template) + bonus + 1f)
                .ToArray();
            List<(RangedTargetEvaluation Shot, int Turns)> prepared = [];
            for (int bonus = 0; bonus <= RangedTargetSelector.FullAimBonusTurns; bonus++)
            {
                prepared.Add((planner.EvaluateRangedTarget(
                    shooter, target, weapon, range, modifiers[bonus], roundPrice: price), bonus + 2));
            }
            RangedTargetEvaluation shootNow = planner.EvaluateRangedTarget(
                shooter, target, weapon, range, 0, roundPrice: price);
            (float grossBest, int grossJ) = prepared
                .Select(p => (p.Shot.Score / p.Turns, p.Turns - 1)).MaxBy(p => p.Item1);
            (float netBest, int netJ) = prepared
                .Select(p => ((p.Shot.Score - price * p.Shot.ShotsToFire) / p.Turns, p.Turns - 1))
                .MaxBy(p => p.Item1);
            // What the plan the charge picks actually removes per turn: its gross score over its turns.
            (RangedTargetEvaluation netShot, int netTurns) = prepared[netJ - 1];
            float grossAtNet = netShot.Score / netTurns;
            sumGrossAtNet += grossAtNet;
            sumGross += grossBest;
            sumNet += netBest;
            sumReadiness += action.ReadinessValue;
            counted++;
            report.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {action.SoldierId,7}  Aim     {targetId,7}  {(term?.TargetId.ToString(CultureInfo.InvariantCulture) ?? "-"),11}"
                + $"  {range,5:F0}  {scarcity,8:F3}  {table,9:F3}  {grossBest,9:F3}  {grossJ,6}  {netBest,7:F3}  {netJ,4}"
                + $"  {action.ReadinessValue,9:F3}  {prepared[0].Shot.ShotsToFire,8}"));
            report.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"      work={work:F1} BV per shooter  rounds={RangedTargetSelector.RoundsCarried(weapon):F0}"
                + $"  scarcity={scarcity:F3}  roundPrice={price:F3}"));
            // Every plan the charge weighs, so a choice can be checked by hand: the pre-roll hit
            // total (skill + modifiers, before the 10.5 +/- 3 roll), the burst the shot planner
            // chose for it, the removal of one burst, and its gross and net value per turn.
            foreach ((RangedTargetEvaluation shot, int turns, string name) in
                new[] { (shootNow, 1, "fire now") }
                    .Concat(prepared.Select(p => (p.Shot, p.Turns, $"aim {p.Turns - 1}, fire"))))
            {
                float net = (shot.Score - price * shot.ShotsToFire) / turns;
                report.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"      {name,-12} hitTotal={shot.PreRollHitTotal,6:F2}  burst={shot.ShotsToFire,2}"
                    + $"  perBurst={shot.Score,6:F3}  turns={turns}  gross/turn={shot.Score / turns,6:F3}"
                    + $"  net/turn={net,7:F3}"));
            }
        }
        report.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"  SUM over {hold.RootActions?.Count ?? 0} actions ({counted} Aim): table sustained {sumTable:F2}, "
            + $"gross aimed {sumGross:F2}, net aimed {sumNet:F2}, readiness {sumReadiness:F2}, "
            + $"gross removal of the net-chosen plans {sumGrossAtNet:F2} (enemy BV per turn)"));
    }

    private static void Report(
        StringBuilder report,
        BattleSquadPlanner planner,
        BattleSquad squad,
        List<BattleSquad> targets)
    {
        report.AppendLine();
        report.AppendLine($"== {squad.Name} ({squad.AbleSoldiers.Count} soldiers) ==");
        report.AppendLine("  shooter  weapon                 target          range  hit1   hitAim  shots aimShots recoil "
            + "takeOut progress  f/hit  hits/kill   unaimed  sustained  targetBV");
        IReadOnlyDictionary<int, SquadPairRemovalRate> rates = planner.GetPairRemovalRates(squad);
        foreach (BattleSquad target in targets)
        {
            if (!rates.TryGetValue(target.Id, out SquadPairRemovalRate rate))
            {
                report.AppendLine($"  (no term against {target.Name})");
                continue;
            }
            float unaimedTotal = 0;
            float sustainedTotal = 0;
            foreach (PairRemovalTerm term in rate.Terms)
            {
                float r = term.ReferenceRange;
                float hit = term.HitProbabilityAt(r, term.ReferenceTargetSpeed);
                // The one-aim-turn plan, for reference; the sustained column uses whichever plan
                // the planner's rule picks (PairRemovalTerm.SustainedRateAt).
                AimPlan? oneAim = term.AimPlans.Count > 0 ? term.AimPlans[0] : null;
                float hitAimed = oneAim is AimPlan plan
                    ? GaussianCalculator.ApproximateNormalCDF(
                        (plan.HitTotal - RemovalMath.HitRollMean) / RemovalMath.HitRollStdDev)
                    : hit;
                float takeOut = term.TakeOutProbabilityAt(r);
                float fraction = term.TakeOutTerms == null
                    ? RemovalMath.CombineRemovalFraction(term.ReferenceTakeOut, term.ReferenceWoundProgress)
                    : RemovalMath.EvaluateRemovalFraction(
                        term.TakeOutTerms,
                        BattleModifiersUtil.CalculateDamageAtRange(term.WeaponTemplate, r));
                float progress = fraction - takeOut;
                float unaimed = term.RemovalAt(r);
                float sustained = term.SustainedRateAt(r);
                unaimedTotal += term.Count * unaimed;
                sustainedTotal += term.Count * sustained;
                report.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {term.ShooterId,7}  {term.WeaponTemplate.Name,-22} {term.TargetId,7}  "
                    + $"{r,8:F0}  {hit,5:F3}  {hitAimed,6:F3}  {term.ReferenceShotsToFire,5}"
                    + $" {oneAim?.ShotsToFire ?? 0,8}"
                    + $" {term.WeaponTemplate.Recoil,6:F1} {takeOut,7:F4} {progress,8:F4} {fraction,7:F4}"
                    + $" {(fraction > 0 ? 1 / fraction : float.PositiveInfinity),9:F1}"
                    + $"  {unaimed,8:F3}  {sustained,9:F3}  {term.TargetBattleValue,8:F1}"));
            }
            float meanTargetValue = target.AbleSoldiers.Average(
                soldier => SquadPlanningServices.BattleValueOf(soldier));
            report.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  SQUAD vs {target.Name}: unaimed {unaimedTotal:F2}/turn "
                + $"({unaimedTotal / meanTargetValue:F3} soldiers), sustained {sustainedTotal:F2}/turn "
                + $"({sustainedTotal / meanTargetValue:F3} soldiers); rate.RateAtRange="
                + $"{rate.RateAtRange(rate.ReferencePairRange):F2}, "
                + $"SustainedRateAtRange={rate.SustainedRateAtRange(rate.ReferencePairRange):F2}"));
        }
    }
}
