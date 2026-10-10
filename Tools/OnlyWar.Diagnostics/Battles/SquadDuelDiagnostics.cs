using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using OnlyWar.Generation.World;
using OnlyWar.Battles;
using OnlyWar.Battles.Aftermath;
using OnlyWar.Battles.Models;
using OnlyWar.Battles.Resolutions;
using OnlyWar.Domain;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Persistence.Database.GameRules;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Diagnostics.Battles;

/// <summary>Which side of a duel or gauntlet battle fills the battle's Attacker role.</summary>
internal enum RoleMode
{
    /// <summary>Odd seeds have A attack, even seeds have B attack, so the roles balance over a batch.</summary>
    Alternate,

    /// <summary>A is always the Attacker (the original behavior).</summary>
    AAttacks,

    /// <summary>B is always the Attacker.</summary>
    BAttacks
}

internal static class RoleModes
{
    /// <summary>Whether side A is the battle's Attacker for this seed.</summary>
    public static bool AAttacksOn(this RoleMode mode, int seed) => mode switch
    {
        RoleMode.AAttacks => true,
        RoleMode.BAttacks => false,
        _ => seed % 2 != 0
    };

    /// <summary>Reads "alternate", "a-attacks" or "b-attacks" from an environment variable.</summary>
    public static RoleMode Read(string name, RoleMode fallback)
    {
        string text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }
        return text.Trim().ToLowerInvariant() switch
        {
            "alternate" => RoleMode.Alternate,
            "a-attacks" => RoleMode.AAttacks,
            "b-attacks" => RoleMode.BAttacks,
            _ => throw new ArgumentException($"{name} must be alternate, a-attacks or b-attacks, not '{text}'.")
        };
    }
}

/// <summary>One squad of a side: a SquadTemplate id, an optional trim, a copy count and a pooled loadout.</summary>
internal sealed record SquadSpec(int TemplateId, int? Soldiers, int Copies, int[] Loadout);

/// <summary>What one side did over a batch of battles, as shares of its soldier-turns.</summary>
internal sealed class BehaviorSummary
{
    public double Fire { get; init; }
    public double Move { get; init; }
    public double Aim { get; init; }
    public double Reload { get; init; }
    public double HitRate { get; init; }
    public double ShotsPerVolley { get; init; }
    public double MeanRange { get; init; }
    public double SwingsPerBattle { get; init; }
}

/// <summary>The outcome of running one scenario over many seeds.</summary>
internal sealed class ProbeResult
{
    /// <summary>Share of battles in which side A held the field (the other side destroyed or broken).</summary>
    public double AHold { get; init; }
    public double BHold { get; init; }
    public double Undecided { get; init; }
    public double ALost { get; init; }
    public double BLost { get; init; }
    public double Turns { get; init; }
    public int MaxTurns { get; init; }
    public int ModelsA { get; init; }
    public int ModelsB { get; init; }
    public string Reasons { get; init; } = "";
    public BehaviorSummary A { get; init; } = new();
    public BehaviorSummary B { get; init; } = new();

    /// <summary>How many battles the figures above average over.</summary>
    public int Seeds { get; init; }

    /// <summary>A's score for fitting: wins plus half of the undecided battles.</summary>
    public double AScore => AHold + 0.5 * Undecided;

    /// <summary>Combines two batches of battles of the same scenario, weighting each by its seeds.</summary>
    public static ProbeResult Merge(ProbeResult first, ProbeResult second)
    {
        double wa = first.Seeds;
        double wb = second.Seeds;
        double total = wa + wb;
        double Mix(double a, double b) => (a * wa + b * wb) / total;
        BehaviorSummary MixBehavior(BehaviorSummary a, BehaviorSummary b) => new()
        {
            Fire = Mix(a.Fire, b.Fire), Move = Mix(a.Move, b.Move), Aim = Mix(a.Aim, b.Aim),
            Reload = Mix(a.Reload, b.Reload), HitRate = Mix(a.HitRate, b.HitRate),
            ShotsPerVolley = Mix(a.ShotsPerVolley, b.ShotsPerVolley), MeanRange = Mix(a.MeanRange, b.MeanRange),
            SwingsPerBattle = Mix(a.SwingsPerBattle, b.SwingsPerBattle)
        };
        return new ProbeResult
        {
            AHold = Mix(first.AHold, second.AHold),
            BHold = Mix(first.BHold, second.BHold),
            Undecided = Mix(first.Undecided, second.Undecided),
            ALost = Mix(first.ALost, second.ALost),
            BLost = Mix(first.BLost, second.BLost),
            Turns = Mix(first.Turns, second.Turns),
            MaxTurns = System.Math.Max(first.MaxTurns, second.MaxTurns),
            ModelsA = first.ModelsA,
            ModelsB = first.ModelsB,
            Reasons = first.Reasons + "; " + second.Reasons,
            A = MixBehavior(first.A, second.A),
            B = MixBehavior(first.B, second.B),
            Seeds = first.Seeds + second.Seeds
        };
    }
}

/// <summary>
/// Opt-in balance diagnostic, not a correctness test: runs squad-against-squad scenarios from the
/// rules database over many seeds and reports who holds the field and the casualties. Gated by
/// RUN_DUEL_DIAGNOSTIC. The ladder runner (SquadLadderDiagnostics) builds on the same probe.
///
/// DUEL_SCENARIOS is a '#'-separated list of "A vs B @ range" (range in yards, which are grid
/// cells; it is the opening range handed to the game's AnnihilationPlacer, which deploys both sides
/// exactly as a meeting engagement does). Each side is a ';'-separated list of squads: "templateId[,n=K][,x=C][,ws=A/B]" where
/// templateId is a SquadTemplate id from the rules database, n trims the squad to K non-leader
/// members, x repeats the squad C times, and ws sets the pooled weapon-set ids handed out to the
/// squad's best shooters (heavy and special weapons). Example, a tactical squad with a heavy
/// bolter and a plasma gun against two PDF squads of nine:
///   "1,ws=5/3 vs 34,n=9,x=2 @ 200".
/// Also read: DUEL_SEEDS (default 100), DUEL_AGGR (Aggressive by default), DUEL_WORKERS (battles run
/// at once, default 8; 1 runs them one after another), DUEL_OUT (the table's path), and DUEL_ROLES,
/// which of the two sides is the battle's Attacker (placed at x = 0, BattleRole.Attacker): "a-attacks"
/// (default, so older numbers stay comparable), "b-attacks", or "alternate" (A attacks on odd seeds,
/// B on even seeds). Whatever the roles, the table is from A's point of view: "A holds" is A holding
/// the field. Squads are built in battle-slot order (the attacker's first), so a role swap of a
/// mirror-symmetric scenario is the same battle with the labels exchanged.
/// The table is written to %TEMP%\GodotOnlyWar\squad-duel.txt unless DUEL_OUT names another file, and
/// the battles per minute to the same name with the extension .throughput.txt.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class SquadDuelDiagnostics
{
    private const int TurnCap = 600;
    private const long MaximumSquadBudget = 1_000_000;

    [Fact]
    public void SquadScenarios_ReportWinRates()
    {
        string scenarios = Environment.GetEnvironmentVariable("DUEL_SCENARIOS");
        if (Environment.GetEnvironmentVariable("RUN_DUEL_DIAGNOSTIC") == null || string.IsNullOrWhiteSpace(scenarios))
        {
            return;
        }

        int seeds = ReadInt("DUEL_SEEDS", 100);
        Aggression aggression = ReadAggression("DUEL_AGGR");
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        StringBuilder table = new();
        table.AppendLine("A holds | B holds | undec. | A lost% | B lost% | cas B:A | turns | scenario");
        (long battlesBefore, long turnsBefore) = Throughput;
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (string scenario in scenarios.Split('#', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            table.AppendLine(RunScenario(blob, scenario, seeds, aggression));
        }
        clock.Stop();
        (long battlesAfter, long turnsAfter) = Throughput;
        // Kept apart from the table, so two runs can be compared line for line without it.
        string throughput = DescribeThroughput(battlesAfter - battlesBefore, turnsAfter - turnsBefore, clock.Elapsed);

        string output = Environment.GetEnvironmentVariable("DUEL_OUT")
            ?? Path.Combine(Path.GetTempPath(), "GodotOnlyWar", "squad-duel.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, table.ToString());
        File.WriteAllText(Path.ChangeExtension(output, ".throughput.txt"), throughput + Environment.NewLine);
    }

    internal static string DescribeThroughput(long battles, long turns, TimeSpan elapsed)
    {
        double minutes = System.Math.Max(elapsed.TotalMinutes, 1e-9);
        return $"{battles} battles, {turns} turns in {elapsed:hh\\:mm\\:ss} "
            + $"({battles / minutes:N1} battles/min, {turns / minutes:N0} turns/min) "
            + $"with {System.Math.Max(1, ReadInt("DUEL_WORKERS", DefaultWorkers))} workers "
            + $"on {Environment.ProcessorCount} logical processors";
    }

    private static string RunScenario(GameRulesBlob blob, string scenario, int seeds, Aggression aggression)
    {
        string[] atRange = scenario.Split('@', StringSplitOptions.TrimEntries);
        int range = int.Parse(atRange[1]);
        string[] sides = atRange[0].Split(" vs ", StringSplitOptions.TrimEntries);
        ProbeResult result = RunProbe(
            blob, ParseSide(sides[0]), ParseSide(sides[1]), range, seeds, aggression,
            roles: RoleModes.Read("DUEL_ROLES", RoleMode.AAttacks));

        string exchange = result.ALost > 0 ? (result.BLost / result.ALost).ToString("N2") : "inf";
        string main = $"{result.AHold * 100,5:N0}% | {result.BHold * 100,5:N0}% | {result.Undecided * 100,5:N0}% | "
            + $"{result.ALost * 100,5:N0} | {result.BLost * 100,5:N0} | {exchange,6} | "
            + $"{result.Turns,4:N0} (max {result.MaxTurns}) | {scenario} [{result.Reasons}]";
        return main + Environment.NewLine + "      A: " + Describe(result.A)
            + Environment.NewLine + "      B: " + Describe(result.B);
    }

    private static string Describe(BehaviorSummary b) =>
        $"fires {b.Fire:P0} of soldier-turns, {b.ShotsPerVolley:N1} shots/volley, hit {b.HitRate:P1} of shots, "
        + $"mean range {b.MeanRange:N0} | swings {b.SwingsPerBattle:N1}/battle | "
        + $"moves {b.Move:P0}, aims {b.Aim:P0}, reloads {b.Reload:P0}";

    /// <summary>
    /// Runs the scenario once per seed and aggregates. The side that holds the field wins, whether
    /// the other side was destroyed or broke and withdrew; a battle nobody holds is undecided.
    ///
    /// Seeds run in parallel (DUEL_WORKERS, default 8). Each battle owns its random streams, so a
    /// seed gives the same battle whichever worker runs it, and the results are summed in seed
    /// order, so the report does not depend on the worker count either. With more than one worker
    /// each battle plans on one thread: parallel battles keep the cores busier than parallel squad
    /// decisions inside one battle, which has only one job per squad and waits for all of them.
    /// </summary>
    internal static ProbeResult RunProbe(
        GameRulesBlob blob,
        List<SquadSpec> specsA,
        List<SquadSpec> specsB,
        int range,
        int seeds,
        Aggression aggression,
        int firstSeed = 1,
        RoleMode roles = RoleMode.AAttacks)
    {
        int workers = System.Math.Max(1, ReadInt("DUEL_WORKERS", DefaultWorkers));
        int planningWorkers = workers > 1 ? 1 : 0;
        BattleRecord[] records = new BattleRecord[seeds];

        // DUEL_TRACE names a file that receives the engine's planning log for the first battle of
        // the run, which is how to see why a squad chose the options it did. The log sink is static,
        // so that battle runs on its own before the others start.
        string tracePath = Environment.GetEnvironmentVariable("DUEL_TRACE");
        string woundLogPath = Environment.GetEnvironmentVariable("DUEL_WOUND_LOG");
        string shotLogPath = Environment.GetEnvironmentVariable("DUEL_SHOT_LOG");
        int start = 0;
        if (!string.IsNullOrWhiteSpace(tracePath) || !string.IsNullOrWhiteSpace(woundLogPath)
            || !string.IsNullOrWhiteSpace(shotLogPath))
        {
            foreach (string path in new[] { tracePath, woundLogPath, shotLogPath }.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            }
            using StreamWriter trace = string.IsNullOrWhiteSpace(tracePath) ? null : new(tracePath);
            if (trace != null) BattleLog.Sink = trace.WriteLine;
            try
            {
                records[0] = RunBattle(
                    blob, specsA, specsB, range, firstSeed, aggression, planningWorkers, roles.AAttacksOn(firstSeed),
                    woundLogPath, shotLogPath);
            }
            finally
            {
                BattleLog.Sink = null;
            }
            start = 1;
        }
        System.Threading.Tasks.Parallel.For(
            start,
            seeds,
            new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = workers },
            index => records[index] = RunBattle(
                blob, specsA, specsB, range, firstSeed + index, aggression, planningWorkers,
                roles.AAttacksOn(firstSeed + index)));

        int aHolds = 0;
        int bHolds = 0;
        int undecided = 0;
        double aLostSum = 0;
        double bLostSum = 0;
        double turnSum = 0;
        int longest = 0;
        int modelsA = records[0].ModelsA;
        int modelsB = records[0].ModelsB;
        Dictionary<string, int> reasons = [];
        SideStats statsA = new() { Battles = seeds };
        SideStats statsB = new() { Battles = seeds };
        foreach (BattleRecord record in records)
        {
            longest = System.Math.Max(longest, record.Turns);
            aLostSum += 1.0 - (double)record.ALeft / record.ModelsA;
            bLostSum += 1.0 - (double)record.BLeft / record.ModelsB;
            turnSum += record.Turns;
            statsA.Add(record.A);
            statsB.Add(record.B);
            if (record.Holder == BattleSide.Attacker) aHolds++;
            else if (record.Holder == BattleSide.Opposing) bHolds++;
            else undecided++;
            reasons[record.Reason] = reasons.GetValueOrDefault(record.Reason) + 1;
        }

        return new ProbeResult
        {
            AHold = (double)aHolds / seeds,
            BHold = (double)bHolds / seeds,
            Undecided = (double)undecided / seeds,
            ALost = aLostSum / seeds,
            BLost = bLostSum / seeds,
            Turns = turnSum / seeds,
            MaxTurns = longest,
            ModelsA = modelsA,
            ModelsB = modelsB,
            Reasons = string.Join(", ", reasons.Select(pair => $"{pair.Key} {pair.Value * 100 / seeds}%")),
            A = statsA.ToSummary(),
            B = statsB.ToSummary(),
            Seeds = seeds
        };
    }

    /// <summary>
    /// One battle's outcome and behavior counts, before it is summed into a probe. Everything is from
    /// side A's point of view: <c>Holder</c> is <see cref="BattleSide.Attacker"/> when A holds the
    /// field and <see cref="BattleSide.Opposing"/> when B does, whichever battle role each had.
    /// </summary>
    internal sealed record BattleRecord(
        int ModelsA, int ModelsB, int ALeft, int BLeft, int Turns, BattleSide? Holder, string Reason,
        SideStats A, SideStats B);

    internal const int DefaultWorkers = 8;

    /// <summary>
    /// Version of how this harness sets a battle up (squad building, placement). Bump it whenever a
    /// change here alters battle results without changing OnlyWar.Battles.dll, so result caches keyed
    /// on the code (the gauntlet's) stop reusing battles fought under the old setup.
    /// 1: original hand-rolled placement, which put the near side's squad leaders in the rear column
    ///    and the far side's in the front; identical squads gave the near (Attacker) slot 53-60% of
    ///    the wins (Design/Active/BattleValueCalibration.md, 7.8).
    /// 2: the same placement with the far side's columns mirrored (2026-10-04).
    /// 3: the game's AnnihilationPlacer, as meeting engagements use (2026-10-04).
    /// </summary>
    internal const int HarnessVersion = 3;

    private static long _battlesRun;
    private static long _turnsRun;

    /// <summary>Battles fought and turns resolved by this process so far, for throughput reports.</summary>
    internal static (long Battles, long Turns) Throughput =>
        (System.Threading.Interlocked.Read(ref _battlesRun), System.Threading.Interlocked.Read(ref _turnsRun));

    /// <summary>
    /// Fights one battle from a seed. Everything the battle draws comes from streams seeded here, so
    /// the result does not depend on which thread runs it, or when.
    /// </summary>
    internal static BattleRecord RunBattle(
        GameRulesBlob blob,
        List<SquadSpec> specsA,
        List<SquadSpec> specsB,
        int range,
        int seed,
        Aggression aggression,
        int planningWorkers,
        bool aAttacks = true,
        string woundLogPath = null,
        string shotLogPath = null)
    {
        // Squad building and the battle each start a fresh stream from the same seed, as the
        // static RNG did when it was reset before each of them.
        int streamSeed = 80_000 + seed;
        OnlyWar.Abstractions.IEntityIdAllocator ids = new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
        SeededRNG buildRandom = new(streamSeed);

        // Squads are built in slot order, the attacker's first, so that a swapped battle is the same
        // battle with the labels exchanged: the attacking slot draws the same squads, the same soldier
        // ids and the same battle stream whichever of A and B fills it. (Building A first always would
        // give A the lower soldier ids, a bias that a role swap would not undo.)
        List<BattleSquad> sideA;
        List<BattleSquad> sideB;
        if (aAttacks)
        {
            sideA = BuildSquads(blob, specsA, ids, "A", buildRandom);
            sideB = BuildSquads(blob, specsB, ids, "B", buildRandom);
        }
        else
        {
            sideB = BuildSquads(blob, specsB, ids, "B", buildRandom);
            sideA = BuildSquads(blob, specsA, ids, "A", buildRandom);
        }
        int modelsA = sideA.Sum(squad => squad.Soldiers.Count);
        int modelsB = sideB.Sum(squad => squad.Soldiers.Count);

        // The battle's Attacker slot (BattleSide.Attacker, the BattleRole.Attacker profile, grid side
        // true, placed at x = 0) holds A when A attacks and B otherwise; the Opposing slot holds the rest.
        List<BattleSquad> attackers = aAttacks ? sideA : sideB;
        List<BattleSquad> defenders = aAttacks ? sideB : sideA;
        BattleGridManager grid = new();
        // The game's own meeting-engagement deployment (BattleEngagementResolver, Meeting): line squads
        // side by side along a front, HQ squads behind it, ranked formations mirrored per side. The
        // Attacker slot is the placer's bottom side (grid side true), as the game's first side is.
        new OnlyWar.Battles.Placers.AnnihilationPlacer(grid, checked((ushort)range))
            .PlaceSquads(attackers, defenders);

        BattleTurnResolver resolver = CreateResolver(
            grid, attackers, defenders, RulesFor(blob), new SeededRNG(streamSeed), aggression, planningWorkers);
        bool completed = false;
        resolver.OnBattleComplete += (_, _) => completed = true;
        int turns = 0;
        for (; turns < TurnCap && !completed; turns++)
        {
            resolver.ProcessNextTurn();
        }
        System.Threading.Interlocked.Increment(ref _battlesRun);
        System.Threading.Interlocked.Add(ref _turnsRun, turns);

        SideStats statsA = new() { Battles = 1 };
        SideStats statsB = new() { Battles = 1 };
        HashSet<int> idsA = sideA.SelectMany(squad => squad.Soldiers).Select(soldier => soldier.Soldier.Id).ToHashSet();
        foreach (BattleTurn recorded in resolver.BattleHistory.Turns)
        {
            int attackerSoldiers = recorded.State.AttackerSquads.Values.Sum(squad => squad.Soldiers.Count);
            int opposingSoldiers = recorded.State.OpposingSquads.Values.Sum(squad => squad.Soldiers.Count);
            statsA.SoldierTurns += aAttacks ? attackerSoldiers : opposingSoldiers;
            statsB.SoldierTurns += aAttacks ? opposingSoldiers : attackerSoldiers;
            foreach (OnlyWar.Battles.Actions.IAction action in recorded.Actions)
            {
                SideStats stats = idsA.Contains(action.ActorId) ? statsA : statsB;
                if (action is OnlyWar.Battles.Actions.ShootAction shot)
                {
                    stats.ShootActions++;
                    stats.Shots += shot.CommittedShots;
                    stats.Hits += shot.HitCount;
                    stats.RangeSum += shot.Range;
                }
                else if (action is OnlyWar.Battles.Actions.MoveAction) stats.MoveActions++;
                else if (action is OnlyWar.Battles.Actions.AimAction) stats.AimActions++;
                else if (action is OnlyWar.Battles.Actions.ReloadRangedWeaponAction) stats.ReloadActions++;
                else if (action is OnlyWar.Battles.Actions.MeleeAttackAction) stats.MeleeActions++;
            }
        }

        BattleStateSnapshot state = resolver.BattleHistory.Turns.Last().State;
        int attackerLeft = state.AttackerSquads.Values.Sum(squad => squad.Soldiers.Count);
        int opposingLeft = state.OpposingSquads.Values.Sum(squad => squad.Soldiers.Count);
        BattleSide? slotHolder = resolver.BattleHistory.Outcome?.SideHoldingField;
        if (slotHolder == null)
        {
            slotHolder = opposingLeft == 0 && attackerLeft > 0 ? BattleSide.Attacker
                : attackerLeft == 0 && opposingLeft > 0 ? BattleSide.Opposing
                : null;
        }

        // Everything reported is from A's point of view: the record's Holder is Attacker when A holds
        // the field and Opposing when B does, whichever battle slot each filled.
        int aLeft = aAttacks ? attackerLeft : opposingLeft;
        int bLeft = aAttacks ? opposingLeft : attackerLeft;
        BattleSide? holder = aAttacks || slotHolder == null
            ? slotHolder
            : slotHolder == BattleSide.Attacker ? BattleSide.Opposing : BattleSide.Attacker;
        string reason = resolver.BattleHistory.Outcome?.EndReason.ToString() ?? "TurnCap";
        if (!string.IsNullOrWhiteSpace(woundLogPath))
        {
            WriteWoundLog(woundLogPath, resolver, sideA, idsA);
        }
        if (!string.IsNullOrWhiteSpace(shotLogPath))
        {
            WriteShotLog(shotLogPath, resolver, idsA);
        }
        return new BattleRecord(modelsA, modelsB, aLeft, bLeft, turns, holder, reason, statsA, statsB);
    }

    /// <summary>
    /// DUEL_SHOT_LOG: every ShootAction in one battle, both sides: turn, side, shooter, target, weapon
    /// template id, rounds planned and fired, the aim multiplier, how many consecutive turns that
    /// shooter had just spent aiming at THAT target (0 when it fired without aiming, or aimed at
    /// someone else), the hits, and the range. Then per side and weapon: volleys, rounds, hits per
    /// round, and the same split by whether the volley followed an aim at its target. It answers
    /// "did the aim reach the shot".
    /// </summary>
    private static void WriteShotLog(string path, BattleTurnResolver resolver, HashSet<int> idsA)
    {
        StringBuilder log = new();
        log.AppendLine("turn\tside\tshooter\ttarget\tweapon\tplanned\tfired\taimMult\taimTurns\thits\trange");
        Dictionary<int, (int Target, int Turns)> aiming = [];
        Dictionary<(string Side, int Weapon, bool Aimed), (int Volleys, int Rounds, int Hits)> summary = [];
        for (int turn = 0; turn < resolver.BattleHistory.Turns.Count; turn++)
        {
            HashSet<int> acted = [];
            foreach (OnlyWar.Battles.Actions.IAction action in resolver.BattleHistory.Turns[turn].Actions)
            {
                if (action is OnlyWar.Battles.Actions.AimAction aim)
                {
                    acted.Add(aim.ActorId);
                    aiming[aim.ActorId] = aiming.TryGetValue(aim.ActorId, out var prior) && prior.Target == aim.TargetId
                        ? (aim.TargetId, prior.Turns + 1)
                        : (aim.TargetId, 1);
                }
                else if (action is OnlyWar.Battles.Actions.ShootAction shot)
                {
                    acted.Add(shot.ShooterId);
                    string side = idsA.Contains(shot.ShooterId) ? "A" : "B";
                    int aimTurns = aiming.TryGetValue(shot.ShooterId, out var held) && held.Target == shot.TargetId
                        ? held.Turns
                        : 0;
                    aiming.Remove(shot.ShooterId);
                    log.AppendLine(string.Join('\t', turn, side, shot.ShooterId, shot.TargetId, shot.WeaponId,
                        shot.NumberOfShots, shot.CommittedShots, shot.AimMultiplier.ToString("F2"), aimTurns,
                        shot.HitCount, shot.Range.ToString("F0")));
                    var key = (side, shot.WeaponId, aimTurns > 0);
                    var sum = summary.GetValueOrDefault(key);
                    summary[key] = (sum.Volleys + 1, sum.Rounds + shot.CommittedShots, sum.Hits + shot.HitCount);
                }
                else
                {
                    acted.Add(action.ActorId);
                }
            }
            // An aim that was not renewed this turn by an AimAction or spent by a shot has lapsed.
            foreach (int shooter in aiming.Keys.Where(id => !acted.Contains(id)).ToList())
            {
                aiming.Remove(shooter);
            }
        }
        log.AppendLine();
        log.AppendLine("SUMMARY side\tweapon\taimed\tvolleys\trounds\thits\thitsPerRound\thitsPerVolley");
        foreach (var entry in summary.OrderBy(e => e.Key.Side).ThenBy(e => e.Key.Weapon).ThenBy(e => e.Key.Aimed))
        {
            log.AppendLine(string.Join('\t', "SUMMARY " + entry.Key.Side, entry.Key.Weapon, entry.Key.Aimed,
                entry.Value.Volleys, entry.Value.Rounds, entry.Value.Hits,
                (entry.Value.Rounds > 0 ? (double)entry.Value.Hits / entry.Value.Rounds : 0).ToString("F3"),
                ((double)entry.Value.Hits / entry.Value.Volleys).ToString("F2")));
        }
        File.WriteAllText(path, log.ToString());
    }

    /// <summary>
    /// DUEL_WOUND_LOG: every wound side A takes in one battle (turn, attack kind, weapon, damage after
    /// armor, hit location, the damage-to-Constitution ratio and the wound level it maps to, using the
    /// thresholds of WoundResolver), a per-weapon/level summary, and the final wound totals of every
    /// side-A model's hit locations. It answers "what actually killed it".
    /// </summary>
    private static void WriteWoundLog(
        string path, BattleTurnResolver resolver, List<BattleSquad> sideA, HashSet<int> idsA)
    {
        StringBuilder log = new();
        Dictionary<(string Kind, string Weapon, string Level), int> summary = [];
        log.AppendLine("turn\tkind\tweapon\tvictim\tlocation\tdamage\tratio\tlevel");
        for (int turn = 0; turn < resolver.BattleHistory.Turns.Count; turn++)
        {
            foreach (OnlyWar.Battles.Actions.IAction action in resolver.BattleHistory.Turns[turn].Actions)
            {
                (string kind, IEnumerable<WoundResolution> wounds) = action switch
                {
                    OnlyWar.Battles.Actions.ShootAction shot => ("shoot", shot.WoundResolutions),
                    OnlyWar.Battles.Actions.MeleeAttackAction melee => ("melee", melee.WoundResolutions),
                    OnlyWar.Battles.Actions.BlastAttackAction blast => ("blast", blast.WoundResolutions),
                    OnlyWar.Battles.Actions.AreaAttackAction area => ("area", area.WoundResolutions),
                    _ => ("", Enumerable.Empty<WoundResolution>())
                };
                foreach (WoundResolution wound in wounds.Where(w => idsA.Contains(w.Suffererer.Soldier.Id)))
                {
                    HitLocationTemplate location = wound.HitLocation.Template;
                    float ratio = (wound.Damage - location.NaturalArmor) * location.WoundMultiplier
                        / wound.Suffererer.Soldier.Constitution;
                    string level = ratio >= 8f ? "Unsurvivable" : ratio >= 4f ? "Mortal" : ratio >= 2f ? "Massive"
                        : ratio >= 1f ? "Critical" : ratio >= 0.5f ? "Major" : ratio >= 0.25f ? "Moderate"
                        : ratio >= 0.125f ? "Minor" : "Negligible";
                    log.AppendLine(string.Join('\t', turn, kind, wound.Weapon?.Name, wound.Suffererer.Soldier.Name,
                        location.Name, wound.Damage.ToString("F1"), ratio.ToString("F3"), level));
                    var key = (kind, wound.Weapon?.Name ?? "?", level);
                    summary[key] = summary.GetValueOrDefault(key) + 1;
                }
            }
        }

        log.AppendLine();
        log.AppendLine("SUMMARY kind\tweapon\tlevel\tcount");
        foreach (var entry in summary.OrderBy(e => e.Key.Kind).ThenBy(e => e.Key.Weapon).ThenBy(e => e.Key.Level))
        {
            log.AppendLine($"SUMMARY {entry.Key.Kind}\t{entry.Key.Weapon}\t{entry.Key.Level}\t{entry.Value}");
        }

        log.AppendLine();
        log.AppendLine("FINAL soldier\tlocation\tvital\twoundTotal\tcrippleAt\tseverAt\tcrippled\tsevered");
        foreach (BattleSoldier soldier in sideA.SelectMany(squad => squad.Soldiers))
        {
            foreach (HitLocation location in soldier.Soldier.Body.HitLocations.Where(l => l.Wounds.WoundTotal > 0))
            {
                log.AppendLine(string.Join('\t', "FINAL " + soldier.Soldier.Name, location.Template.Name,
                    location.Template.IsVital, $"0x{location.Wounds.WoundTotal:X}",
                    $"0x{(uint)location.Template.CrippleWound:X}", $"0x{(uint)location.Template.SeverWound:X}",
                    location.IsCrippled, location.IsSevered));
            }
        }
        File.WriteAllText(path, log.ToString());
    }

    // One GameRulesData per loaded blob. Building it is what GameRulesLoader does after reading the
    // database, so this skips a full database read per battle, and the battle now sees the same
    // template objects the squads were built from.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameRulesBlob, GameRulesData> Rules = new();

    private static GameRulesData RulesFor(GameRulesBlob blob) => Rules.GetValue(blob, loaded => new GameRulesData(loaded));

    /// <summary>Counts the models a side fields, without running a battle.</summary>
    internal static int CountModels(GameRulesBlob blob, List<SquadSpec> specs) => Measure(blob, specs).Models;

    /// <summary>
    /// The model count and the summed template Battle Value of a side, without running a battle.
    /// The summed value is only a starting guess for the ladder search; it never reaches a result.
    /// </summary>
    internal static (int Models, int Value) Measure(GameRulesBlob blob, List<SquadSpec> specs)
    {
        OnlyWar.Abstractions.IEntityIdAllocator ids = new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
        List<BattleSquad> squads = BuildSquads(blob, specs, ids, "count", new SeededRNG(80_000));
        return (
            squads.Sum(squad => squad.Soldiers.Count),
            squads.SelectMany(squad => squad.Soldiers).Sum(soldier => soldier.Soldier.Template.BattleValue));
    }

    internal static List<SquadSpec> ParseSide(string text)
    {
        List<SquadSpec> specs = [];
        foreach (string squadText in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = squadText.Split(',', StringSplitOptions.TrimEntries);
            int templateId = int.Parse(parts[0]);
            int? soldiers = null;
            int copies = 1;
            int[] loadout = [];
            foreach (string part in parts.Skip(1))
            {
                string[] pair = part.Split('=', 2);
                switch (pair[0])
                {
                    case "n": soldiers = int.Parse(pair[1]); break;
                    case "x": copies = int.Parse(pair[1]); break;
                    case "ws": loadout = pair[1].Split('/').Select(int.Parse).ToArray(); break;
                    default: throw new ArgumentException($"Unknown squad option '{part}'.");
                }
            }
            specs.Add(new SquadSpec(templateId, soldiers, copies, loadout));
        }
        return specs;
    }

    internal static Aggression ReadAggression(string name) =>
        Enum.TryParse(Environment.GetEnvironmentVariable(name), out Aggression parsed) ? parsed : Aggression.Aggressive;

    internal static int ReadInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out int value) ? value : fallback;

    internal static List<BattleSquad> BuildSquads(
        GameRulesBlob blob,
        List<SquadSpec> specs,
        OnlyWar.Abstractions.IEntityIdAllocator ids,
        string prefix,
        IRNG random)
    {
        List<BattleSquad> squads = [];
        foreach (SquadSpec spec in specs)
        {
            SquadTemplate template = blob.Factions
                .Where(faction => faction.SquadTemplates.ContainsKey(spec.TemplateId))
                .Select(faction => faction.SquadTemplates[spec.TemplateId])
                .First();
            for (int copy = 0; copy < spec.Copies; copy++)
            {
                // An unlimited budget fills every element to its maximum, including elements that
                // would otherwise roll a size per seed, so a spec without n= is always the full squad.
                Squad squad = SquadFactory.GenerateSquadWithinBudget(
                    template, MaximumSquadBudget, random, ids, $"{prefix} {template.Name} {squads.Count + 1}");
                if (spec.Soldiers.HasValue)
                {
                    List<ISoldier> troops = squad.Members.Where(member => !member.Template.IsSquadLeader).ToList();
                    foreach (ISoldier extra in troops.Skip(spec.Soldiers.Value))
                    {
                        squad.RemoveSquadMember(extra);
                    }
                }
                if (spec.Loadout.Length > 0)
                {
                    squad.Loadout = spec.Loadout.Select(id => blob.WeaponSets[id]).ToList();
                }
                squads.Add(new BattleSquad(false, squad));
            }
        }
        return squads;
    }

    private static BattleTurnResolver CreateResolver(
        BattleGridManager grid,
        IList<BattleSquad> attackers,
        IList<BattleSquad> defenders,
        GameRulesData rules,
        IRNG random,
        Aggression aggression,
        int planningWorkers)
    {
        BattleAftermathDependencies aftermath = new(new Date(1, 1, 1), random, NoOpSink.Instance);
        BattleExecutionContext execution = new(rules, random, aftermath, planningWorkers);
        return new BattleTurnResolver(
            grid,
            attackers,
            defenders,
            region: null,
            execution,
            new BattleSideProfile(aggression, BattleRole.Attacker),
            new BattleSideProfile(aggression, BattleRole.Defender));
    }

    /// <summary>Per-side action totals summed over every seed's battle history.</summary>
    internal sealed class SideStats
    {
        public int Battles;
        public double SoldierTurns;
        public double ShootActions;
        public double Shots;
        public double Hits;
        public double RangeSum;
        public double MeleeActions;
        public double MoveActions;
        public double AimActions;
        public double ReloadActions;

        /// <summary>Adds one battle's counts. The caller sets <see cref="Battles"/> for the total.</summary>
        public void Add(SideStats other)
        {
            SoldierTurns += other.SoldierTurns;
            ShootActions += other.ShootActions;
            Shots += other.Shots;
            Hits += other.Hits;
            RangeSum += other.RangeSum;
            MeleeActions += other.MeleeActions;
            MoveActions += other.MoveActions;
            AimActions += other.AimActions;
            ReloadActions += other.ReloadActions;
        }

        public BehaviorSummary ToSummary()
        {
            double turns = System.Math.Max(1, SoldierTurns);
            return new BehaviorSummary
            {
                Fire = ShootActions / turns,
                Move = MoveActions / turns,
                Aim = AimActions / turns,
                Reload = ReloadActions / turns,
                HitRate = Shots > 0 ? Hits / Shots : 0,
                ShotsPerVolley = ShootActions > 0 ? Shots / ShootActions : 0,
                MeanRange = ShootActions > 0 ? RangeSum / ShootActions : 0,
                SwingsPerBattle = MeleeActions / System.Math.Max(1, Battles)
            };
        }
    }

    private sealed class NoOpSink : IPlayerBattleAftermathSink
    {
        public static NoOpSink Instance { get; } = new();

        public void MoveToFallenBrothers(PlayerSoldier soldier) { }
        public void AddRecoveredGeneseed(float purity) { }
        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) { }
    }
}
