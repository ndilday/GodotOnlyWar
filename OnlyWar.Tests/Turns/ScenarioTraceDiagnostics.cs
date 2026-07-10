using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

// Throwaway balance-diagnostic (NOT a correctness test): generate the opening "Promised World"
// scenario across several seeds, snapshot the board the player inherits, then run each sector
// forward with an IDLE player (no orders) to observe the abandonment/lapse dynamic and the
// swarm's spread. Writes per-seed traces + a combined CSV to the scratchpad for human analysis.
//
// It is gated by RUN_SCENARIO_TRACE so normal test runs discover it but return immediately.
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class ScenarioTraceDiagnostics
{
    private static readonly int[] Seeds = { 1, 2, 7, 11, 42, 101, 2024, 31337 };
    private const int IdleTurns = 15;

    private static readonly string OutDir =
        Path.Combine(Path.GetTempPath(), "GodotOnlyWar", "scenario-trace");

    private readonly Date _date = new(39, 500, 1);

    // Focused probe for the "frozen Imperial pocket blocks the lapse" question: run several
    // Pending-prone seeds forward and, each turn, dump every default-faction region-faction on the
    // promised world with its region's carrying capacity, so we can see whether a surviving pocket's
    // population is pinned to the region's (low, slowly-recovering) carrying capacity. Gated by
    // RUN_POCKET_DUMP.
    [Fact]
    [Trait("Category", "Diagnostics")]
    [Trait("Category", "Slow")]
    public void DumpImperialPockets()
    {
        if (Environment.GetEnvironmentVariable("RUN_POCKET_DUMP") == null)
        {
            return;
        }

        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        Directory.CreateDirectory(OutDir);

        int[] seeds = { 1, 11, 42, 2024, 31337 };
        const int turns = 30;

        foreach (int seed in seeds)
        {
            GameRulesData data = new();
            GameDataSingleton.Instance.LoadGameDataFromBlob(data, _date, null);
            Sector sector = SectorBuilder.GenerateSector(seed, data, _date, $"Pocket {seed}");
            GameDataSingleton.Instance.LoadGameDataFromBlob(data, _date, sector);
            Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);
            Faction imp = data.DefaultFaction;
            Faction tyr = data.SectorFactions.Invader;

            StringBuilder sb = new();
            sb.AppendLine($"=== Seed {seed} '{promised.Name}' ({promised.Template.Name}) ===");

            for (int turn = 0; turn <= turns; turn++)
            {
                if (turn > 0) new TurnController().ProcessTurn(sector);

                long impTotal = 0;
                foreach (Region r in promised.Regions)
                {
                    if (r.RegionFactionMap.TryGetValue(imp.Id, out RegionFaction irf))
                    {
                        impTotal += irf.Population;
                    }
                }
                // Only dump once the Imperial presence is small enough to be the pocket regime.
                if (impTotal == 0 || impTotal > 20000) continue;

                sb.AppendLine($"turn {turn}: state={sector.Scenario.State}, imperialTotalPop={impTotal:N0}");
                foreach (Region r in promised.Regions)
                {
                    if (!r.RegionFactionMap.TryGetValue(imp.Id, out RegionFaction irf)) continue;
                    long tyrPop = r.RegionFactionMap.TryGetValue(tyr.Id, out RegionFaction trf) ? trf.Population : 0;
                    sb.AppendLine(
                        $"    region {r.Id,2} imp[pub={irf.IsPublic,-5} pop={irf.Population,8:N0} gar={irf.Garrison,6:N0}] "
                        + $"cap={r.CarryingCapacity,12:N0} maxCap={r.MaximumCarryingCapacity,12:N0} tyrPopHere={tyrPop,12:N0}");
                }
            }

            File.WriteAllText(Path.Combine(OutDir, $"pocket-{seed}.txt"), sb.ToString());
        }
    }

    [Fact]
    [Trait("Category", "Diagnostics")]
    [Trait("Category", "Slow")]
    public void RunSeeds_TraceOpeningAndIdleForwardSim()
    {
        // Gated so a normal `dotnet test` returns instantly instead of paying the ~6-minute
        // multi-seed forward-sim. To run it: set RUN_SCENARIO_TRACE=1 in the environment, e.g.
        //   RUN_SCENARIO_TRACE=1 dotnet test --filter FullyQualifiedName~ScenarioTraceDiagnostics
        if (Environment.GetEnvironmentVariable("RUN_SCENARIO_TRACE") == null)
        {
            return;
        }

        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        Directory.CreateDirectory(OutDir);

        StringBuilder csv = new();
        csv.AppendLine("seed,turn,state,postLandingWeeks,tyrRegions,tyrPop,tyrGarrison,tyrMilStr,"
            + "cultRegions,cultPop,impPop,impGarrison,blightPct,planetPop,battles,strategicBattles");

        foreach (int seed in Seeds)
        {
            RunOneSeed(seed, csv);
        }

        File.WriteAllText(Path.Combine(OutDir, "summary.csv"), csv.ToString());
    }

    private void RunOneSeed(int seed, StringBuilder csv)
    {
        GameRulesData data = new();
        GameDataSingleton.Instance.LoadGameDataFromBlob(data, _date, null);

        // Capture the generation-time trace (the pre/post-landing SimulatePlanetForward sims emit
        // Info/Debug), so we can read how long the swarm fed and how the cult war went.
        StringBuilder gen = new();
        int postLandingWeeks = -1;
        int simCount = 0;
        GameLog.MinimumLevel = GameLogLevel.Debug;
        GameLog.Sink = (level, msg) =>
        {
            gen.AppendLine($"[{level}] {msg}");
            // "SimulatePlanetForward '<name>': N turns" — first is pre-landing, second is post.
            int idx = msg.IndexOf("SimulatePlanetForward", StringComparison.Ordinal);
            if (idx >= 0)
            {
                simCount++;
                if (simCount == 2)
                {
                    int colon = msg.LastIndexOf(": ", StringComparison.Ordinal);
                    int turnsWord = msg.IndexOf(" turns", StringComparison.Ordinal);
                    if (colon >= 0 && turnsWord > colon)
                    {
                        string num = msg.Substring(colon + 2, turnsWord - colon - 2).Trim();
                        int.TryParse(num, out postLandingWeeks);
                    }
                }
            }
        };

        Sector sector;
        try
        {
            sector = SectorBuilder.GenerateSector(seed, data, _date, $"Seed {seed} Chapter");
        }
        finally
        {
            GameLog.Sink = null;
            GameLog.MinimumLevel = GameLogLevel.Off;
        }

        // Register the fully generated sector so ProcessTurn can resolve the singleton.
        GameDataSingleton.Instance.LoadGameDataFromBlob(data, _date, sector);
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);

        StringBuilder report = new();
        report.AppendLine($"=== Seed {seed} — promised world '{promised.Name}' "
            + $"(template {promised.Template.Name}, {promised.Regions.Length} regions) ===");
        report.AppendLine($"post-landing feed weeks: {postLandingWeeks}");
        report.AppendLine();
        report.AppendLine("Idle forward-sim (no player orders):");
        report.AppendLine(HeaderLine());

        Snapshot(data, sector, promised, seed, turn: 0, postLandingWeeks,
            battles: 0, strategic: 0, csv, report);

        for (int turn = 1; turn <= IdleTurns; turn++)
        {
            TurnController controller = new();
            controller.ProcessTurn(sector);
            Snapshot(data, sector, promised, seed, turn, postLandingWeeks,
                controller.MissionContexts.Count, controller.StrategicCombatResults.Count, csv, report);
        }

        report.AppendLine();
        report.AppendLine("--- generation trace ---");
        report.Append(gen);
        File.WriteAllText(Path.Combine(OutDir, $"seed-{seed}.txt"), report.ToString());
    }

    private static string HeaderLine() =>
        $"{"turn",4} {"state",-8} {"tyrReg",6} {"tyrPop",12} {"tyrGar",10} {"tyrMil",12} "
        + $"{"cultReg",7} {"cultPop",12} {"impPop",14} {"impGar",12} {"blight%",7} {"battles",7}";

    private void Snapshot(GameRulesData data, Sector sector, Planet promised, int seed, int turn,
                          int postLandingWeeks, int battles, int strategic, StringBuilder csv,
                          StringBuilder report)
    {
        Faction tyr = data.SectorFactions.Invader;
        Faction cult = data.SectorFactions.Infiltrator;
        Faction imp = data.DefaultFaction;

        long tyrPop = 0, tyrGar = 0, tyrMil = 0, cultPop = 0, impPop = 0, impGar = 0;
        int tyrRegions = 0, cultRegions = 0;
        long capNow = 0, capMax = 0;

        foreach (Region region in promised.Regions)
        {
            capNow += region.CarryingCapacity;
            capMax += region.MaximumCarryingCapacity;
            if (region.RegionFactionMap.TryGetValue(tyr.Id, out RegionFaction trf)
                && (trf.Population > 0 || trf.Garrison > 0))
            {
                tyrRegions++;
                tyrPop += trf.Population;
                tyrGar += trf.Garrison;
                tyrMil += trf.MilitaryStrength;
            }
            if (region.RegionFactionMap.TryGetValue(cult.Id, out RegionFaction crf)
                && (crf.Population > 0 || crf.Garrison > 0))
            {
                cultRegions++;
                cultPop += crf.Population;
            }
            if (region.RegionFactionMap.TryGetValue(imp.Id, out RegionFaction irf))
            {
                impPop += irf.Population;
                impGar += irf.Garrison;
            }
        }

        double blightPct = capMax > 0 ? 100.0 * (capMax - capNow) / capMax : 0.0;
        string state = sector.Scenario.State.ToString();

        report.AppendLine(
            $"{turn,4} {state,-8} {tyrRegions,6} {tyrPop,12:N0} {tyrGar,10:N0} {tyrMil,12:N0} "
            + $"{cultRegions,7} {cultPop,12:N0} {impPop,14:N0} {impGar,12:N0} {blightPct,7:F1} {battles,7}");

        csv.AppendLine(string.Join(",", new[]
        {
            seed.ToString(), turn.ToString(), state, postLandingWeeks.ToString(),
            tyrRegions.ToString(), tyrPop.ToString(), tyrGar.ToString(), tyrMil.ToString(),
            cultRegions.ToString(), cultPop.ToString(), impPop.ToString(), impGar.ToString(),
            blightPct.ToString("F1", CultureInfo.InvariantCulture),
            promised.Population.ToString(), battles.ToString(), strategic.ToString()
        }));
    }
}
