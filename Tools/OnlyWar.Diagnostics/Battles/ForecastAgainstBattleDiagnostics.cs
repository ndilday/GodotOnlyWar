using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using OnlyWar.Battles;
using OnlyWar.Battles.Actions;
using OnlyWar.Battles.Models;
using OnlyWar.Domain;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Diagnostics.Battles;

/// <summary>
/// Opt-in calibration of the engagement potential's forecast against the battles it is planning
/// (2026-10-23, Design/Reference/ForecastCalibration.md section 3: the forecast's fire looked two to
/// three times the battles'). Fights FORECAST_AUDIT_SCENARIO (duel syntax, "A vs B @ range",
/// assault:N allowed) for FORECAST_AUDIT_SEEDS battles on one planning thread with
/// EngagementPotential.RootForecastSink set, and compares every squad's root forecast -- the
/// continuation that sets its potential -- with what then happened, from the battle history's
/// end-of-turn squad rosters (cleaned of that turn's casualties) and withdrawal roles. Everything
/// is a fraction, so the force-share pools of the forecast's lanes cancel:
///
/// ONE TURN AHEAD, by range band: the share of the enemy's remaining value the forecast expects its
/// side to remove this turn (each squad's forecast assumes the rest of its side fires as it does;
/// averaged over the side's squads by value) against the share actually lost; the same for own
/// losses.
///
/// FROM TURN 1: the forecast made on the first planning turn (turn index 1, so turn 0 has a
/// before-state), followed over its horizon: predicted cumulative enemy loss, enemy broken share
/// and own loss against the actual enemy loss, enemy routed share and own loss, turn by turn.
///
/// FRONTAGE: on each turn a side lands melee strikes, its distinct strikers and the distinct defenders
/// they reached, and per striker the strikes planned, hits landed and moved-penalty share
/// (Design/Reference/ForecastCalibration.md section 4).
///
/// Output: FORECAST_AUDIT_OUT folder (default %TEMP%\GodotOnlyWar\forecast-audit),
/// forecast-audit.txt. DUEL_AGGR applies as in the duel tool.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class ForecastAgainstBattleDiagnostics
{
    private static readonly float[] Bands = [2, 10, 25, 50, 100, 200, 300, 450, float.PositiveInfinity];
    private const int HorizonShown = 30;
    private const int FrontageTurnsShown = 6;
    // Loss levels of the leader table: 10%, 20%, ... 50%.
    private const int LeaderLossLevels = 5;

    [Fact]
    public void Forecast_AgainstWhatHappened()
    {
        string scenario = Environment.GetEnvironmentVariable("FORECAST_AUDIT_SCENARIO");
        if (Environment.GetEnvironmentVariable("RUN_FORECAST_AUDIT") == null || string.IsNullOrWhiteSpace(scenario))
        {
            return;
        }
        int seeds = SquadDuelDiagnostics.ReadInt("FORECAST_AUDIT_SEEDS", 6);
        SquadDuelDiagnostics.ApplyPlannerSwitches();
        string[] parts = scenario.Split('@');
        string[] sides = parts[0].Split(" vs ", StringSplitOptions.TrimEntries);
        int range = SquadDuelDiagnostics.ParseRange(parts.Length > 1 ? parts[1].Trim() : "200");
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();

        Dictionary<(string Side, int Band), (int Count, double Predicted, double Actual)> enemyStep = [];
        Dictionary<(string Side, int Band), (int Count, double Predicted, double Actual)> ownStep = [];
        Dictionary<(string Side, int K), double[]> horizon = [];
        Dictionary<(string Side, int C), double[]> frontage = [];
        List<string> traceLines = [];
        // Per side and cumulative-loss band: [checks, live fail fraction, forecast fail fraction,
        // live rout results, routed after coercion, forecast hazard, mob support, local outnumber,
        // routing visible].
        Dictionary<(string Side, int Band), double[]> checks = [];
        // Per side: [squads, checks, battle turns, leaders lost, cumulative loss when lost,
        // squads past half losses with the leader alive].
        Dictionary<string, double[]> cadence = [];
        // Per side and loss level: [squads that reached it, leader dead there, their mean loss there].
        Dictionary<(string Side, int Level), double[]> leaderCurve = [];
        // Per side and range band, root states with a Close continuation: [count, stay / own pool,
        // close / own pool, close better, close own lost, close own broken, close enemy removed,
        // close contact turn (count with contact in [8]), advanced (CloseToContact, RunToward,
        // JogToward or StepForward chosen that turn)].
        Dictionary<(string Side, int Band), double[]> continuations = [];
        // Per side and range band, root states: [count, able soldiers at turn start, shooters with a
        // table term, forecast volleys a turn, forecast gross removal a turn, volleys fired, soldiers
        // that aimed, soldiers that moved, rounds fired, the forecast's first-turn outgoing].
        Dictionary<(string Side, int Band), double[]> fire = [];
        // FORECAST_AUDIT_ENGAGE=1: the planner's ENGAGE_EVAL lines (every option's score terms), the
        // chosen option and role per side and range band, and to engage-trace.txt every option of
        // the side-A squads whose root state starts in the band at FORECAST_AUDIT_ENGAGE_BAND yards
        // (default 200), with the same squad's three turns before.
        bool engage = Environment.GetEnvironmentVariable("FORECAST_AUDIT_ENGAGE") == "1";
        float engageBand = SquadDuelDiagnostics.ReadInt("FORECAST_AUDIT_ENGAGE_BAND", 200);
        Dictionary<(string Side, int Band, string Role, string Kind), int> chosenKinds = [];
        StringBuilder engageTrace = new();
        StringBuilder notes = new();

        foreach (int seed in Enumerable.Range(1, seeds))
        {
            List<(int Turn, EngagementPotential.RootForecast Forecast)> forecasts = [];
            HashSet<int> seenThisTurn = [];
            int planningTurn = 0;
            int rootEvaluations = 0;
            // FORECAST_AUDIT_TRACE=1: the first battle's first squad's root forecasts, lanes and
            // turns, to forecast-trace.txt.
            bool trace = seed == 1 && Environment.GetEnvironmentVariable("FORECAST_AUDIT_TRACE") == "1";
            if (trace)
            {
                EngagementPotential.ForecastTraceSquadId = -1;
                EngagementPotential.ForecastTraceSink = line => traceLines.Add($"[turn {planningTurn}] {line}");
            }
            EngagementPotential.RootForecastSink = forecast =>
            {
                if (trace && EngagementPotential.ForecastTraceSquadId == -1)
                {
                    EngagementPotential.ForecastTraceSquadId = forecast.SquadId;
                }
                // The root is evaluated more than once per turn; the first is kept.
                rootEvaluations++;
                if (seenThisTurn.Add(forecast.SquadId))
                {
                    forecasts.Add((planningTurn, forecast));
                }
            };
            void BeforeTurn(int turn)
            {
                planningTurn = turn;
                seenThisTurn.Clear();
            }
            List<(string Side, BattleMoraleService.MoraleCheckObservation Check, float Hazard, float ForecastFail,
                float HazardRouting, float FailRouting)> observed = [];
            BattleMoraleService.CheckObserver = check =>
            {
                string side = check.Squad.Name.StartsWith("A ", StringComparison.Ordinal) ? "A" : "B";
                // The forecast's own check at the live inputs: the leader as he is (prior loss
                // carries the cumulative casualties, so the forecast's expected leader loss is 0
                // unless he is dead), the live mob support, no coercion.
                List<BattleSoldier> able = check.Squad.AbleSoldiers;
                if (able.Count == 0)
                {
                    return;
                }
                float[] resolves = able.Select(soldier => BattleMoraleEvaluator.EgoToResolve(soldier.Soldier.Ego)).ToArray();
                float mean = resolves.Average();
                float spread = (float)Math.Sqrt(resolves.Sum(value => (value - mean) * (value - mean)) / resolves.Length);
                BattleSoldier leader = check.Squad.SquadLeader;
                SquadBreakProfile profile = new(
                    true,
                    able.Count,
                    mean,
                    spread,
                    leader != null,
                    leader == null ? 0f : BattleMoraleEvaluator.EgoToResolve(leader.Soldier.Ego),
                    check.LeaderDead,
                    check.CumulativeCasualty,
                    check.MobSupport);
                float hazard = EngagementExchangeForecast.BreakHazard(profile, check.CasualtyThisTurn, 0f, check.ForceDisadvantage);
                float fail = EngagementExchangeForecast.FailMargin(profile, check.CasualtyThisTurn, 0f, check.ForceDisadvantage).Fail;
                // The same check with the live shock from routing friends in sight added (negative
                // relief adds stress): does that term close the gap?
                SquadBreakProfile withRouting = profile with
                {
                    ShockRelief = profile.ShockRelief - (MoraleConstants.ShockRoutingVisibleWeight * check.RoutingVisible),
                };
                float hazardRouting = EngagementExchangeForecast.BreakHazard(withRouting, check.CasualtyThisTurn, 0f, check.ForceDisadvantage);
                float failRouting = EngagementExchangeForecast.FailMargin(withRouting, check.CasualtyThisTurn, 0f, check.ForceDisadvantage).Fail;
                observed.Add((side, check, hazard, fail, hazardRouting, failRouting));
            };
            Dictionary<(int Turn, int Squad), List<string>> evaluations = [];
            if (engage)
            {
                BattleLog.Sink = line =>
                {
                    if (!line.StartsWith("ENGAGE_EVAL ", StringComparison.Ordinal))
                    {
                        return;
                    }
                    var key = (int.Parse(Field(line, "turn"), CultureInfo.InvariantCulture),
                        int.Parse(Field(line, "squad"), CultureInfo.InvariantCulture));
                    if (!evaluations.TryGetValue(key, out List<string> list))
                    {
                        evaluations[key] = list = [];
                    }
                    list.Add(line);
                };
            }
            BattleTurnResolver resolver = null;
            try
            {
                SquadDuelDiagnostics.RunBattle(
                    blob,
                    SquadDuelDiagnostics.ParseSide(sides[0]),
                    SquadDuelDiagnostics.ParseSide(sides[1]),
                    range,
                    seed,
                    SquadDuelDiagnostics.ReadAggression("DUEL_AGGR"),
                    planningWorkers: 1,
                    aAttacks: seed % 2 == 1,
                    onFinished: finished => resolver = finished,
                    beforeTurn: BeforeTurn);
            }
            finally
            {
                EngagementPotential.RootForecastSink = null;
                EngagementPotential.ForecastTraceSink = null;
                BattleMoraleService.CheckObserver = null;
                BattleLog.Sink = null;
            }

            IReadOnlyList<BattleTurn> turns = resolver.BattleHistory.Turns;
            notes.AppendLine($"  seed {seed}: {turns.Count} battle turns, {forecasts.Count} root forecasts kept "
                + $"of {rootEvaluations} root evaluations");
            Dictionary<int, string> sideOf = [];
            foreach (BattleSquadSnapshot squad in turns[0].State.AttackerSquads.Values
                .Concat(turns[0].State.OpposingSquads.Values))
            {
                sideOf[squad.Id] = squad.Name.StartsWith("A ", StringComparison.Ordinal) ? "A" : "B";
            }
            // End-of-turn value alive, and routed, per side.
            (double Alive, double Routed) Strength(int turn, string side)
            {
                BattleStateSnapshot state = turns[turn].State;
                double alive = 0;
                double routed = 0;
                foreach (BattleSquadSnapshot squad in state.AttackerSquads.Values.Concat(state.OpposingSquads.Values))
                {
                    if (!sideOf.TryGetValue(squad.Id, out string owner) || owner != side)
                    {
                        continue;
                    }
                    double value = squad.Soldiers.Sum(soldier => (double)Math.Max(1, soldier.Soldier.StrategicValue));
                    alive += value;
                    if (squad.WithdrawalRole == WithdrawalRole.Routing)
                    {
                        routed += value;
                    }
                }
                return (alive, routed);
            }
            string Other(string side) => side == "A" ? "B" : "A";

            // One turn ahead.
            foreach (var group in forecasts.Where(entry => entry.Turn >= 1 && entry.Turn < turns.Count)
                .GroupBy(entry => (entry.Turn, Side: sideOf.GetValueOrDefault(entry.Forecast.SquadId, "?"))))
            {
                (int turn, string side) = group.Key;
                if (side == "?")
                {
                    continue;
                }
                double weight = 0;
                double enemyPredicted = 0;
                double ownPredicted = 0;
                double rangeSum = 0;
                foreach ((_, EngagementPotential.RootForecast forecast) in group)
                {
                    if (forecast.Turns.Count == 0 || forecast.OwnPool <= 0)
                    {
                        continue;
                    }
                    EngagementExchangeForecast.TurnTrace first = forecast.Turns[0];
                    double enemyBefore = first.EnemyRemaining + first.Outgoing;
                    double ownBefore = first.OwnRemaining + first.Incoming;
                    weight += forecast.OwnPool;
                    enemyPredicted += forecast.OwnPool * (enemyBefore > 0 ? first.Outgoing / enemyBefore : 0);
                    ownPredicted += forecast.OwnPool * (ownBefore > 0 ? first.Incoming / ownBefore : 0);
                    rangeSum += forecast.OwnPool * first.Range;
                }
                if (weight <= 0)
                {
                    continue;
                }
                double enemyBeforeActual = Strength(turn - 1, Other(side)).Alive;
                double ownBeforeActual = Strength(turn - 1, side).Alive;
                double enemyActual = enemyBeforeActual > 0
                    ? (enemyBeforeActual - Strength(turn, Other(side)).Alive) / enemyBeforeActual
                    : 0;
                double ownActual = ownBeforeActual > 0
                    ? (ownBeforeActual - Strength(turn, side).Alive) / ownBeforeActual
                    : 0;
                int band = Array.FindIndex(Bands, edge => rangeSum / weight < edge);
                var e = enemyStep.GetValueOrDefault((side, band));
                enemyStep[(side, band)] = (e.Count + 1, e.Predicted + enemyPredicted / weight, e.Actual + enemyActual);
                var o = ownStep.GetValueOrDefault((side, band));
                ownStep[(side, band)] = (o.Count + 1, o.Predicted + ownPredicted / weight, o.Actual + ownActual);
            }

            // Stay against Close at each root state.
            foreach ((int turn, EngagementPotential.RootForecast forecast) in forecasts)
            {
                if (forecast.CloseTurns == null || forecast.CloseTurns.Count == 0 || forecast.OwnPool <= 0
                    || turn >= turns.Count || !sideOf.TryGetValue(forecast.SquadId, out string side))
                {
                    continue;
                }
                EngagementExchangeForecast.TurnTrace first = forecast.CloseTurns[0];
                EngagementExchangeForecast.TurnTrace last = forecast.CloseTurns[^1];
                double pool = first.EnemyRemaining + first.Outgoing;
                int band = Array.FindIndex(Bands, edge => forecast.Turns.Count > 0 && forecast.Turns[0].Range < edge);
                if (band < 0)
                {
                    continue;
                }
                double[] row = continuations.GetValueOrDefault((side, band)) ?? new double[10];
                row[0] += 1;
                row[1] += forecast.StayValue / forecast.OwnPool;
                row[2] += forecast.CloseValue / forecast.OwnPool;
                row[3] += forecast.CloseValue > forecast.StayValue ? 1 : 0;
                row[4] += 1 - (last.OwnRemaining / forecast.OwnPool);
                row[5] += 1 - last.OwnSurvival;
                row[6] += pool > 0 ? 1 - (last.EnemyRemaining / pool) : 0;
                int contact = forecast.CloseTurns.ToList().FindIndex(trace => trace.Range <= 2f);
                if (contact >= 0)
                {
                    row[7] += contact;
                    row[8] += 1;
                }
                // A routing squad's RunToward is toward its escape, not the enemy (2026-10-26: 22 of
                // the 50 PDF "advances" at 200-300 yd against Warriors were routs).
                BattleSquadSnapshot after = turns[turn].State.AttackerSquads.Values
                    .Concat(turns[turn].State.OpposingSquads.Values)
                    .FirstOrDefault(squad => squad.Id == forecast.SquadId);
                EngagementOptionKind? chosen = after?.WithdrawalRole == WithdrawalRole.Routing
                    ? null
                    : after?.LastEngagementOptionKind;
                row[9] += chosen is EngagementOptionKind.CloseToContact or EngagementOptionKind.RunToward
                    or EngagementOptionKind.JogToward or EngagementOptionKind.StepForward ? 1 : 0;
                continuations[(side, band)] = row;
            }

            // Fire cadence: the standing cycles the squad's table credits against what its soldiers
            // did on the turn the forecast was made for.
            foreach ((int turn, EngagementPotential.RootForecast forecast) in forecasts)
            {
                if (turn < 1 || turn >= turns.Count || forecast.Turns.Count == 0
                    || !sideOf.TryGetValue(forecast.SquadId, out string side))
                {
                    continue;
                }
                BattleSquadSnapshot before = turns[turn - 1].State.AttackerSquads.Values
                    .Concat(turns[turn - 1].State.OpposingSquads.Values)
                    .FirstOrDefault(squad => squad.Id == forecast.SquadId);
                if (before == null || before.Soldiers.Count == 0)
                {
                    continue;
                }
                HashSet<int> members = [.. before.Soldiers.Select(soldier => soldier.Id)];
                List<IAction> own = turns[turn].Actions.Where(action => members.Contains(action.ActorId)).ToList();
                int band = Array.FindIndex(Bands, edge => forecast.Turns[0].Range < edge);
                double[] row = fire.GetValueOrDefault((side, band)) ?? new double[10];
                row[0] += 1;
                row[1] += before.Soldiers.Count;
                row[2] += forecast.Cycles.Shooters;
                row[3] += forecast.Cycles.VolleysPerTurn;
                row[4] += forecast.Cycles.RatePerTurn;
                List<ShootAction> shots = own.OfType<ShootAction>().ToList();
                row[5] += shots.Count;
                row[6] += own.OfType<AimAction>().Select(action => action.ActorId).Distinct().Count();
                row[7] += own.OfType<MoveAction>().Select(action => action.ActorId).Distinct().Count();
                row[8] += shots.Sum(shot => shot.CommittedShots);
                row[9] += forecast.Turns[0].Outgoing;
                fire[(side, band)] = row;
            }

            // The chosen option and role at each root state, and the trace of the band's side-A squads.
            if (engage)
            {
                Dictionary<(int Turn, int Squad), float> rangeAt = forecasts
                    .Where(entry => entry.Forecast.Turns.Count > 0)
                    .GroupBy(entry => (entry.Turn, Squad: entry.Forecast.SquadId))
                    .ToDictionary(group => group.Key, group => group.First().Forecast.Turns[0].Range);
                int engageIndex = Array.FindIndex(Bands, edge => engageBand < edge);
                foreach (((int turn, int squadId), float startRange) in rangeAt.OrderBy(entry => entry.Key.Squad).ThenBy(entry => entry.Key.Turn))
                {
                    if (!sideOf.TryGetValue(squadId, out string side)
                        || !evaluations.TryGetValue((turn, squadId), out List<string> lines))
                    {
                        continue;
                    }
                    int band = Array.FindIndex(Bands, edge => startRange < edge);
                    string chosen =lines.FirstOrDefault(line => Field(line, "chosen") == "true");
                    if (chosen != null)
                    {
                        var key = (side, band, Field(chosen, "role"), Field(chosen, "kind"));
                        chosenKinds[key] = chosenKinds.GetValueOrDefault(key) + 1;
                    }
                    if (side != "A" || band != engageIndex)
                    {
                        continue;
                    }
                    for (int back = 3; back >= 0; back--)
                    {
                        int at = turn - back;
                        if (!evaluations.TryGetValue((at, squadId), out List<string> earlier))
                        {
                            continue;
                        }
                        engageTrace.AppendLine(string.Create(CultureInfo.InvariantCulture,
                            $"seed {seed} turn {at} squad {squadId} range {(rangeAt.TryGetValue((at, squadId), out float r) ? r : float.NaN):F1}{(back == 0 ? "  <== in band" : "")}"));
                        foreach (string line in earlier)
                        {
                            engageTrace.AppendLine("  " + line);
                        }
                    }
                    engageTrace.AppendLine();
                }
            }

            // Squads that have routed by each turn (a routed squad later leaves the rosters, so
            // the routed column above undercounts), as a share of the side's starting value.
            Dictionary<int, double> startValue = turns[0].State.AttackerSquads.Values
                .Concat(turns[0].State.OpposingSquads.Values)
                .ToDictionary(squad => squad.Id,
                    squad => squad.Soldiers.Sum(soldier => (double)Math.Max(1, soldier.Soldier.StrategicValue)));
            HashSet<int> everRouted = [];
            List<HashSet<int>> routedBy = [];
            foreach (BattleTurn battleTurn in turns)
            {
                foreach (BattleSquadSnapshot squad in battleTurn.State.AttackerSquads.Values
                    .Concat(battleTurn.State.OpposingSquads.Values))
                {
                    if (squad.WithdrawalRole == WithdrawalRole.Routing)
                    {
                        everRouted.Add(squad.Id);
                    }
                }
                routedBy.Add([.. everRouted]);
            }
            double EverRouted(int turn, string side)
            {
                double total = startValue.Where(entry => sideOf.GetValueOrDefault(entry.Key) == side).Sum(entry => entry.Value);
                double routed = routedBy[turn].Where(id => sideOf.GetValueOrDefault(id) == side)
                    .Sum(id => startValue.GetValueOrDefault(id));
                return total > 0 ? routed / total : 0;
            }

            // Live morale checks against the forecast's check at the same inputs.
            foreach ((string side, BattleMoraleService.MoraleCheckObservation check, float hazard, float forecastFail,
                float hazardRouting, float failRouting) in observed)
            {
                int band = Math.Min(3, (int)(check.CumulativeCasualty * 4));
                double[] row = checks.GetValueOrDefault((side, band)) ?? new double[11];
                row[9] += hazardRouting;
                row[10] += failRouting;
                row[0] += 1;
                row[1] += check.FailFraction;
                row[2] += forecastFail;
                row[3] += check.Outcome == MoraleState.Routing ? 1 : 0;
                row[4] += check.Routed ? 1 : 0;
                row[5] += hazard;
                row[6] += check.MobSupport;
                row[7] += check.LocalOutnumber;
                row[8] += check.RoutingVisible;
                checks[(side, band)] = row;
            }
            foreach (string side in new[] { "A", "B" })
            {
                List<int> squadIds = sideOf.Where(entry => entry.Value == side).Select(entry => entry.Key).ToList();
                double[] row = cadence.GetValueOrDefault(side) ?? new double[6];
                row[0] += squadIds.Count;
                row[1] += observed.Count(entry => entry.Side == side);
                row[2] += turns.Count;
                foreach (int id in squadIds)
                {
                    List<BattleMoraleService.MoraleCheckObservation> own = observed
                        .Where(entry => entry.Check.Squad.Id == id)
                        .Select(entry => entry.Check)
                        .ToList();
                    BattleMoraleService.MoraleCheckObservation? lost = own.Where(check => check.LeaderDead)
                        .Select(check => (BattleMoraleService.MoraleCheckObservation?)check)
                        .FirstOrDefault();
                    if (lost.HasValue)
                    {
                        row[3] += 1;
                        row[4] += lost.Value.CumulativeCasualty;
                    }
                    if (own.Any(check => !check.LeaderDead && check.CumulativeCasualty >= 0.5f))
                    {
                        row[5] += 1;
                    }
                    // Leader survival by loss: at the first check at or past each loss level,
                    // whether the leader was already dead.
                    for (int level = 1; level <= LeaderLossLevels; level++)
                    {
                        float loss = level / (float)(LeaderLossLevels * 2);
                        BattleMoraleService.MoraleCheckObservation? reached = own
                            .Where(check => check.CumulativeCasualty >= loss)
                            .Select(check => (BattleMoraleService.MoraleCheckObservation?)check)
                            .FirstOrDefault();
                        if (!reached.HasValue)
                        {
                            continue;
                        }
                        double[] curve = leaderCurve.GetValueOrDefault((side, level)) ?? new double[3];
                        curve[0] += 1;
                        curve[1] += reached.Value.LeaderDead ? 1 : 0;
                        curve[2] += reached.Value.CumulativeCasualty;
                        leaderCurve[(side, level)] = curve;
                    }
                }
                cadence[side] = row;
            }

            // From turn 1: [predicted enemy lost, actual, predicted enemy broken, actual routed,
            // predicted own lost, actual, count, predicted own broken, own ever routed].
            foreach (string side in new[] { "A", "B" })
            {
                List<EngagementPotential.RootForecast> first = forecasts
                    .Where(entry => entry.Turn == 1 && sideOf.GetValueOrDefault(entry.Forecast.SquadId) == side
                        && entry.Forecast.Turns.Count > 0 && entry.Forecast.OwnPool > 0)
                    .Select(entry => entry.Forecast)
                    .ToList();
                if (first.Count == 0 || turns.Count < 2)
                {
                    continue;
                }
                double enemyStart = Strength(0, Other(side)).Alive;
                double ownStart = Strength(0, side).Alive;
                double weight = first.Sum(forecast => forecast.OwnPool);
                for (int k = 0; k < HorizonShown; k++)
                {
                    double enemyLost = 0;
                    double enemyBroken = 0;
                    double ownLost = 0;
                    double ownBroken = 0;
                    foreach (EngagementPotential.RootForecast forecast in first)
                    {
                        EngagementExchangeForecast.TurnTrace start = forecast.Turns[0];
                        double pool = start.EnemyRemaining + start.Outgoing;
                        EngagementExchangeForecast.TurnTrace at = forecast.Turns[Math.Min(k, forecast.Turns.Count - 1)];
                        enemyLost += forecast.OwnPool * (pool > 0 ? 1 - (at.EnemyRemaining / pool) : 0);
                        enemyBroken += forecast.OwnPool * (1 - at.EnemySurvival);
                        ownLost += forecast.OwnPool * (1 - (at.OwnRemaining / forecast.OwnPool));
                        ownBroken += forecast.OwnPool * (1 - at.OwnSurvival);
                    }
                    int turn = Math.Min(1 + k, turns.Count - 1);
                    (double enemyAlive, double enemyRouted) = Strength(turn, Other(side));
                    double ownAlive = Strength(turn, side).Alive;
                    double[] row = horizon.GetValueOrDefault((side, k)) ?? new double[9];
                    row[7] += ownBroken / weight;
                    row[8] += EverRouted(turn, side);
                    row[0] += enemyLost / weight;
                    row[1] += enemyStart > 0 ? 1 - (enemyAlive / enemyStart) : 0;
                    row[2] += enemyBroken / weight;
                    row[3] += enemyStart > 0 ? enemyRouted / enemyStart : 0;
                    row[4] += ownLost / weight;
                    row[5] += ownStart > 0 ? 1 - (ownAlive / ownStart) : 0;
                    row[6] += 1;
                    horizon[(side, k)] = row;
                }
            }

            // Frontage: on each turn the side lands melee strikes, its distinct strikers and the
            // distinct defenders they reached, against the able soldiers of both sides at turn start.
            Dictionary<int, string> sideOfSoldier = [];
            foreach (BattleSquadSnapshot squad in turns[0].State.AttackerSquads.Values
                .Concat(turns[0].State.OpposingSquads.Values))
            {
                foreach (BattleSoldierSnapshot soldier in squad.Soldiers)
                {
                    sideOfSoldier[soldier.Id] = sideOf.GetValueOrDefault(squad.Id, "?");
                }
            }
            int Able(int turn, string side) => turns[turn].State.AttackerSquads.Values
                .Concat(turns[turn].State.OpposingSquads.Values)
                .Where(squad => sideOf.GetValueOrDefault(squad.Id) == side)
                .Sum(squad => squad.Soldiers.Count);
            foreach (string side in new[] { "A", "B" })
            {
                int contactTurn = 0;
                for (int turn = 1; turn < turns.Count; turn++)
                {
                    List<MeleeAttackAction> strikes = turns[turn].Actions
                        .OfType<MeleeAttackAction>()
                        .Where(action => action.TargetedDefenderIds.Count > 0
                            && sideOfSoldier.GetValueOrDefault(action.ActorId) == side)
                        .ToList();
                    if (strikes.Count == 0)
                    {
                        if (contactTurn > 0)
                        {
                            contactTurn++;
                        }
                        continue;
                    }
                    int strikers = strikes.Select(action => action.ActorId).Distinct().Count();
                    int struck = strikes.SelectMany(action => action.TargetedDefenderIds).Distinct().Count();
                    int c = Math.Min(contactTurn, FrontageTurnsShown - 1);
                    double[] row = frontage.GetValueOrDefault((side, c)) ?? new double[8];
                    row[0] += Able(turn - 1, side);
                    row[1] += Able(turn - 1, Other(side));
                    row[2] += strikers;
                    row[3] += struck;
                    row[4] += 1;
                    row[5] += strikes.Sum(action => action.StrikePlans.Count);
                    row[6] += strikes.Sum(action => action.WoundResolutions.Count);
                    row[7] += strikes.Count(action => action.UsesMovementAttackPenalty);
                    frontage[(side, c)] = row;
                    contactTurn++;
                }
            }
        }

        StringBuilder report = new();
        report.AppendLine($"Forecast against battle: {scenario}, {seeds} battles. Fractions of the side's remaining value.");
        report.Append(notes);
        report.AppendLine();
        report.AppendLine("ONE TURN AHEAD (the side's squads' root forecasts, value-weighted, against the turn that followed):");
        report.AppendLine($"  {"side",4} {"band",9} {"turns",5} | {"enemy lost: forecast",21} {"actual",7} {"ratio",6} | {"own lost: forecast",19} {"actual",7} {"ratio",6}");
        foreach (((string side, int band), var e) in enemyStep.OrderBy(entry => entry.Key.Side).ThenBy(entry => entry.Key.Band))
        {
            var o = ownStep[(side, band)];
            string label = band == 0 ? "<2" : $"{Bands[band - 1]:0}-{(float.IsInfinity(Bands[band]) ? "" : Bands[band].ToString("0"))}";
            report.AppendLine($"  {side,4} {label,9} {e.Count,5} | {P(e.Predicted / e.Count),21} {P(e.Actual / e.Count),7} {Ratio(e.Predicted, e.Actual),6} | "
                + $"{P(o.Predicted / o.Count),19} {P(o.Actual / o.Count),7} {Ratio(o.Predicted, o.Actual),6}");
        }
        report.AppendLine();
        report.AppendLine("FROM TURN 1 (the first planning turn's forecast followed over its horizon; cumulative, of the start):");
        report.AppendLine("  (own broken f is the forecast's own break chance; own routed a, the share of the side's starting value routed by then)");
        report.AppendLine($"  {"side",4} {"k",3} | {"enemy lost f",12} {"a",5} | {"enemy broken f",14} {"routed a",8} | {"own lost f",10} {"a",5} | {"own broken f",12} {"routed a",8}");
        foreach (((string side, int k), double[] row) in horizon.OrderBy(entry => entry.Key.Side).ThenBy(entry => entry.Key.K))
        {
            double n = row[6];
            report.AppendLine($"  {side,4} {k,3} | {P(row[0] / n),12} {P(row[1] / n),5} | {P(row[2] / n),14} {P(row[3] / n),8} | "
                + $"{P(row[4] / n),10} {P(row[5] / n),5} | {P(row[7] / n),12} {P(row[8] / n),8}");
        }
        report.AppendLine();
        report.AppendLine("MORALE CHECKS (live checks, not rallies, by the squad's cumulative casualties; forecast = the forecast's");
        report.AppendLine("  check at the same inputs: the leader as he is, the live mob support, no coercion, no local outnumbering,");
        report.AppendLine("  routing friends or command aura; +rtg = the same with the live routing-friends shock added):");
        report.AppendLine($"  {"side",4} {"loss",7} {"checks",6} | {"fail live",9} {"forecast",8} {"+rtg",6} | {"rout live",9} {"forecast",8} {"+rtg",6} {"routed",6} | {"support",7} {"outnum",6} {"rtgVis",6}");
        string[] lossBands = ["0-25%", "25-50%", "50-75%", "75%+"];
        foreach (((string side, int band), double[] row) in checks.OrderBy(entry => entry.Key.Side).ThenBy(entry => entry.Key.Band))
        {
            double n = row[0];
            report.AppendLine($"  {side,4} {lossBands[band],7} {n,6:0} | {P(row[1] / n),9} {P(row[2] / n),8} {P(row[10] / n),6} | {P(row[3] / n),9} {P(row[5] / n),8} {P(row[9] / n),6} {P(row[4] / n),6} | "
                + $"{row[6] / n,7:0.00} {row[7] / n,6:0.00} {row[8] / n,6:0.00}");
        }
        report.AppendLine();
        report.AppendLine("CONTINUATIONS (root states with a Close continuation, by range: values as shares of the own pool;");
        report.AppendLine("  the Close forecast's own loss, own break chance and enemy removed at its end, its contact turn; and the");
        report.AppendLine("  share of those turns the squad advanced, routing squads not counted):");
        report.AppendLine($"  {"side",4} {"band",9} {"n",5} | {"stay",6} {"close",6} {"close>",6} | {"ownLost",7} {"broken",6} {"enemyRm",7} {"contact",7} | {"advanced",8}");
        foreach (((string side, int band), double[] row) in continuations.OrderBy(entry => entry.Key.Side).ThenBy(entry => entry.Key.Band))
        {
            double n = row[0];
            string label = band == 0 ? "<2" : $"{Bands[band - 1]:0}-{(float.IsInfinity(Bands[band]) ? "" : Bands[band].ToString("0"))}";
            report.AppendLine($"  {side,4} {label,9} {n,5:0} | {row[1] / n,6:0.00} {row[2] / n,6:0.00} {P(row[3] / n),6} | "
                + $"{P(row[4] / n),7} {P(row[5] / n),6} {P(row[6] / n),7} {(row[8] > 0 ? (row[7] / row[8]).ToString("0.0", CultureInfo.InvariantCulture) : "none"),7} | {P(row[9] / n),8}");
        }
        report.AppendLine();
        report.AppendLine("FIRE CADENCE (root states by range; per able soldier-turn: shooters with a table term, the volleys their");
        report.AppendLine("  standing cycles credit, and what the soldiers did on that turn; rate = the cycles' gross removal a turn");
        report.AppendLine("  per squad, out1 = the forecast's first-turn outgoing per squad):");
        report.AppendLine($"  {"side",4} {"band",9} {"n",5} {"able",5} | {"termed",6} {"vol f",6} {"vol a",6} {"f/a",5} | {"aimed",6} {"moved",6} {"rnd/vol",7} | {"rate",6} {"out1",6}");
        foreach (((string side, int band), double[] row) in fire.OrderBy(entry => entry.Key.Side).ThenBy(entry => entry.Key.Band))
        {
            double n = row[0];
            double able = row[1];
            string label = band == 0 ? "<2" : $"{Bands[band - 1]:0}-{(float.IsInfinity(Bands[band]) ? "" : Bands[band].ToString("0"))}";
            report.AppendLine($"  {side,4} {label,9} {n,5:0} {able / n,5:0.0} | {P(row[2] / able),6} {P(row[3] / able),6} {P(row[5] / able),6} {Ratio(row[3], row[5]),5} | "
                + $"{P(row[6] / able),6} {P(row[7] / able),6} {(row[5] > 0 ? row[8] / row[5] : 0),7:0.0} | {row[4] / n,6:0.00} {row[9] / n,6:0.00}");
        }
        if (engage)
        {
            report.AppendLine();
            report.AppendLine("CHOSEN OPTIONS (root states by range: the squad's role and the option it chose, count):");
            foreach (var group in chosenKinds.GroupBy(entry => (entry.Key.Side, entry.Key.Band))
                .OrderBy(group => group.Key.Side).ThenBy(group => group.Key.Band))
            {
                int band = group.Key.Band;
                string label = band == 0 ? "<2" : $"{Bands[band - 1]:0}-{(float.IsInfinity(Bands[band]) ? "" : Bands[band].ToString("0"))}";
                report.AppendLine($"  {group.Key.Side} {label,9}: " + string.Join(", ", group
                    .OrderByDescending(entry => entry.Value)
                    .Select(entry => $"{entry.Key.Role}/{entry.Key.Kind} {entry.Value}")));
            }
        }
        report.AppendLine();
        report.AppendLine("CADENCE AND LEADERS (per side, all battles):");
        foreach ((string side, double[] row) in cadence.OrderBy(entry => entry.Key))
        {
            report.AppendLine($"  {side}: {row[1] / row[0]:0.0} checks per squad in {row[2] / seeds:0.0} battle turns; leaders lost in {row[3]:0} of {row[0]:0} squads, "
                + $"at {(row[3] > 0 ? P(row[4] / row[3]) : "n/a")} cumulative loss on average; {row[5]:0} squads checked past half losses with the leader alive");
        }
        report.AppendLine("  Leader dead by loss (first check at or past each level; the forecast's model is a chance equal to the loss):");
        foreach (((string side, int level), double[] curve) in leaderCurve.OrderBy(entry => entry.Key.Side).ThenBy(entry => entry.Key.Level))
        {
            report.AppendLine($"    {side} at {P(level / (double)(LeaderLossLevels * 2)),4}+: {curve[1]:0} of {curve[0]:0} squads, "
                + $"{P(curve[1] / curve[0])} dead at a mean loss of {P(curve[2] / curve[0])}");
        }

        report.AppendLine();
        report.AppendLine("FRONTAGE (turns on which the side landed melee strikes, counted from its first; means over battles;");
        report.AppendLine("  k = strikers per enemy able at turn start, the frontage rule's constant):");
        report.AppendLine("  Per striker: strikes planned, hits landed, and the share of strike actions under the moved penalty.");
        report.AppendLine($"  {"side",4} {"c",3} {"n",3} | {"own able",8} {"enemy able",10} | {"strikers",8} {"struck",6} | {"strikers/own",12} {"struck/enemy",12} {"k",5} | {"planned",7} {"hits",5} {"moved",6}");
        foreach (((string side, int c), double[] row) in frontage.OrderBy(entry => entry.Key.Side).ThenBy(entry => entry.Key.C))
        {
            double n = row[4];
            report.AppendLine($"  {side,4} {(c == FrontageTurnsShown - 1 ? c + "+" : c.ToString()),3} {n,3:0} | {row[0] / n,8:0.0} {row[1] / n,10:0.0} | "
                + $"{row[2] / n,8:0.0} {row[3] / n,6:0.0} | {P(row[2] / row[0]),12} {P(row[3] / row[1]),12} {row[2] / row[1],5:0.00} | "
                + $"{row[5] / row[2],7:0.00} {row[6] / row[2],5:0.00} {P(row[7] / row[2]),6}");
        }

        string directory = Environment.GetEnvironmentVariable("FORECAST_AUDIT_OUT")
            ?? Path.Combine(Path.GetTempPath(), "GodotOnlyWar", "forecast-audit");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "forecast-audit.txt"), report.ToString());
        if (engageTrace.Length > 0)
        {
            File.WriteAllText(Path.Combine(directory, "engage-trace.txt"), engageTrace.ToString());
        }
        if (traceLines.Count > 0)
        {
            File.WriteAllLines(Path.Combine(directory, "forecast-trace.txt"), traceLines);
        }
    }

    // The value of "name=" in a BattleDecisionTrace line (fields are space-separated).
    private static string Field(string line, string name)
    {
        int start = line.IndexOf(" " + name + "=", StringComparison.Ordinal);
        if (start < 0)
        {
            return "";
        }
        start += name.Length + 2;
        int end = line.IndexOf(' ', start);
        return end < 0 ? line[start..] : line[start..end];
    }

    private static string P(double value) => (100 * value).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Ratio(double predicted, double actual) =>
        actual > 0 ? (predicted / actual).ToString("0.00", CultureInfo.InvariantCulture) : "n/a";
}
