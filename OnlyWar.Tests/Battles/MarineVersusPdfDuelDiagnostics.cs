using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using OnlyWar.Generation.World;
using OnlyWar.Battles;
using OnlyWar.Battles.Aftermath;
using OnlyWar.Battles.Models;
using OnlyWar.Domain;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Persistence.Database.GameRules;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Opt-in balance diagnostic, not a correctness test: one boltgun marine against a few lasgun PDF
/// troopers, started a fixed distance apart, run over many seeds. It answers "how many PDF troopers
/// is one marine worth in this engine", which is the real-fight check on the stored Battle Values.
/// Gated by RUN_DUEL_DIAGNOSTIC so a normal run returns immediately. Settings come from the
/// environment: DUEL_SEEDS (default 100), DUEL_PDF and DUEL_RANGE (comma lists; defaults 5 and 200;
/// a cell is a yard). The table is written to %TEMP%\GodotOnlyWar\duel.txt.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class MarineVersusPdfDuelDiagnostics
{
    private const int PlayerFactionId = 1;
    private const int MarineSoldierTemplateId = 18;
    private const int PdfSoldierTemplateId = 52;
    private const int BoltgunWeaponSetId = 0;
    private const int LasgunWeaponSetId = 24;
    private const int TacticalSquadTemplateId = 1;
    private const int PdfInfantrySquadTemplateId = 34;
    private const int TurnCap = 600;

    [Fact]
    public void MarineVersusLasgunPdf_ReportsWinRate()
    {
        if (Environment.GetEnvironmentVariable("RUN_DUEL_DIAGNOSTIC") == null)
        {
            return;
        }

        int seeds = ReadInt("DUEL_SEEDS", 100);
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        StringBuilder table = new();
        table.AppendLine("range(yd) pdf | marine first | pdf wiped, marine alive | neither | pdf left at marine death | marine death turn");
        foreach (int range in ReadList("DUEL_RANGE", [200]))
        {
            foreach (int pdfCount in ReadList("DUEL_PDF", [5]))
            {
                table.AppendLine(RunDuel(blob, pdfCount, range, seeds));
            }
        }
        string directory = Path.Combine(Path.GetTempPath(), "GodotOnlyWar");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "duel.txt"), table.ToString());
    }

    private static string RunDuel(GameRulesBlob blob, int pdfCount, int range, int seeds)
    {
        Faction marines = blob.Factions.Single(faction => faction.Id == PlayerFactionId);
        Faction imperial = blob.Factions.Single(faction => faction.IsDefaultFaction);
        WeaponSet boltgun = blob.WeaponSets[BoltgunWeaponSetId];
        WeaponSet lasgun = blob.WeaponSets[LasgunWeaponSetId];
        ArmorTemplate powerArmor = marines.SquadTemplates[TacticalSquadTemplateId].Armor;
        ArmorTemplate lightArmor = imperial.SquadTemplates[PdfInfantrySquadTemplateId].Armor;

        SquadTemplate marineTemplate = new(
            990_001, "Duel Marine", boltgun, [], powerArmor,
            [new SquadTemplateElement(marines.SoldierTemplates[MarineSoldierTemplateId], 1, 1,
                defaultWeapons: boltgun, defaultArmor: powerArmor)],
            SquadTypes.None)
        { Faction = marines };
        SquadTemplate pdfTemplate = new(
            990_002, "Duel PDF", lasgun, [], lightArmor,
            [new SquadTemplateElement(imperial.SoldierTemplates[PdfSoldierTemplateId], (byte)pdfCount, (byte)pdfCount,
                defaultWeapons: lasgun, defaultArmor: lightArmor)],
            SquadTypes.None)
        { Faction = imperial };

        int marineFirst = 0;
        int pdfFirst = 0;
        int neither = 0;
        int marineSurvivorsTotal = 0;
        List<int> pdfLeftWhenMarineDied = [];
        List<int> marineDeathTurns = [];
        StringBuilder log = new();

        for (int seed = 1; seed <= seeds; seed++)
        {
            RNG.Reset(80_000 + seed);
            OnlyWar.Abstractions.IEntityIdAllocator ids = new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
            BattleSquad marine = new(false, SquadFactory.GenerateSquad(marineTemplate, new StaticRNG(), ids, "Marine"));
            BattleSquad pdf = new(false, SquadFactory.GenerateSquad(pdfTemplate, new StaticRNG(), ids, "PDF"));

            BattleGridManager grid = new();
            grid.PlaceAt(marine.Soldiers[0], side: true, (0, 0));
            for (int i = 0; i < pdf.Soldiers.Count; i++)
            {
                grid.PlaceAt(pdf.Soldiers[i], side: false, (range, i));
            }

            BattleTurnResolver resolver = CreateResolver(grid, [marine], [pdf], 80_000 + seed);
            bool completed = false;
            resolver.OnBattleComplete += (_, _) => completed = true;
            int marineDeathTurn = -1;
            int pdfWipeTurn = -1;
            int pdfAtMarineDeath = -1;
            for (int turn = 1; turn <= TurnCap && !completed; turn++)
            {
                resolver.ProcessNextTurn();
                BattleStateSnapshot state = resolver.BattleHistory.Turns.Last().State;
                int marinesLeft = state.AttackerSquads.Values.Sum(squad => squad.Soldiers.Count);
                int pdfLeft = state.OpposingSquads.Values.Sum(squad => squad.Soldiers.Count);
                if (marineDeathTurn < 0 && marinesLeft == 0)
                {
                    marineDeathTurn = turn;
                    pdfAtMarineDeath = pdfLeft;
                }
                if (pdfWipeTurn < 0 && pdfLeft == 0)
                {
                    pdfWipeTurn = turn;
                }
            }

            string result;
            if (marineDeathTurn > 0 && (pdfWipeTurn < 0 || marineDeathTurn <= pdfWipeTurn))
            {
                marineFirst++;
                pdfLeftWhenMarineDied.Add(pdfAtMarineDeath);
                marineDeathTurns.Add(marineDeathTurn);
                result = $"marine died turn {marineDeathTurn}, {pdfAtMarineDeath} PDF left";
            }
            else if (pdfWipeTurn > 0)
            {
                pdfFirst++;
                marineSurvivorsTotal++;
                result = $"PDF wiped turn {pdfWipeTurn}, marine alive";
            }
            else
            {
                neither++;
                result = $"neither wiped ({resolver.BattleHistory.Outcome?.EndReason})";
            }
            log.AppendLine($"seed {seed}: {result}");
        }

        string leftAtDeath = pdfLeftWhenMarineDied.Count > 0 ? pdfLeftWhenMarineDied.Average().ToString("N2") : "-";
        string deathTurn = marineDeathTurns.Count > 0 ? marineDeathTurns.Average().ToString("N1") : "-";
        return $"{range,5} {pdfCount,3} | {marineFirst,4} | {pdfFirst,4} | {neither,4} | {leftAtDeath} | {deathTurn}   ({seeds} seeds)";
    }

    private static int ReadInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out int value) ? value : fallback;

    private static int[] ReadList(string name, int[] fallback)
    {
        string raw = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(raw)
            ? fallback
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(int.Parse).ToArray();
    }

    private static BattleTurnResolver CreateResolver(
        BattleGridManager grid,
        IList<BattleSquad> attackers,
        IList<BattleSquad> defenders,
        int seed)
    {
        GameRulesData rules = GameRulesLoader.Load(RulesDatabaseFixture.DatabasePath);
        RNG.Reset(seed);
        StaticRNG random = new();
        BattleAftermathDependencies aftermath = new(new Date(1, 1, 1), random, NoOpSink.Instance);
        BattleExecutionContext execution = new(rules, random, aftermath);
        return new BattleTurnResolver(
            grid,
            attackers,
            defenders,
            region: null,
            execution,
            new BattleSideProfile(Aggression.Aggressive, BattleRole.Attacker),
            new BattleSideProfile(Aggression.Aggressive, BattleRole.Defender));
    }

    private sealed class NoOpSink : IPlayerBattleAftermathSink
    {
        public static NoOpSink Instance { get; } = new();

        public void MoveToFallenBrothers(PlayerSoldier soldier) { }
        public void AddRecoveredGeneseed(float purity) { }
        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) { }
    }
}
