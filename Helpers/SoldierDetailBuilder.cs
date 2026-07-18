using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

public class SoldierDetailBuilder
{
    private readonly SoldierDossierService _dossierService = new();

    public ChapterBrowserDetail Build(ISoldier soldier, bool includeOpenFullRecordAction, bool includeSquadInTitle = false)
    {
        List<ChapterBrowserDetailCard> cards = [];

        if (soldier is PlayerSoldier playerSoldier)
        {
            SoldierDossier dossier = _dossierService.BuildDossier(
                playerSoldier,
                richTextInjury: false,
                currentDate: GameDataSingleton.Instance.Date,
                sector: GameDataSingleton.Instance.Sector);
            // Left grid, two columns: Posting/Sergeant, Battle/Honors, Injury.
            // Service Record is full-height on the right with its own scroll.
            cards.Add(new ChapterBrowserDetailCard("map_pin", "Posting", "Assignment", FormatPairs(dossier.Data)));
            cards.Add(new ChapterBrowserDetailCard("training", "Sergeant Report", "Recommendation", dossier.SergeantReport));
            cards.Add(new ChapterBrowserDetailCard("threat", "Battle History", "Combat record", FormatPairs(dossier.CombatRecord)));
            cards.Add(new ChapterBrowserDetailCard("award", "Honors", "Awards", FormatLines(dossier.Awards, "No awards recorded."), Scrollable: true));
            cards.Add(new ChapterBrowserDetailCard("medical", "Injury Report", playerSoldier.CanFight ? "Fit report" : "Recovery report", dossier.InjuryReport));
            cards.Add(new ChapterBrowserDetailCard("archive", "Service Record", "Full history", FormatLines(dossier.History, "No history recorded."), Scrollable: true, FullHeight: true));
        }
        else
        {
            cards.Add(new ChapterBrowserDetailCard("archive", "Record", "Chronicle", "Detailed battle history is only available for player soldiers."));
        }

        string title = $"{soldier.Template.Name} {soldier.Name}";
        if (includeSquadInTitle && soldier.AssignedSquad != null)
        {
            title += $" - {soldier.AssignedSquad.Name}, {soldier.AssignedSquad.ParentUnit.Name}";
        }

        return new ChapterBrowserDetail(
            GetSoldierIconKey(soldier),
            title,
            $"{(soldier.CanFight ? "Available for duty" : "Wounded or impaired")} - {SquadLocationFormatter.Format(soldier.AssignedSquad)}",
            [],
            cards,
            includeOpenFullRecordAction ? "Open Full Record" : null,
            includeOpenFullRecordAction ? "archive" : null);
    }

    public static string GetSoldierIconKey(ISoldier soldier)
    {
        if (!soldier.CanFight)
        {
            return "wounded";
        }
        if (soldier.Template.IsSquadLeader)
        {
            return "rank_sergeant";
        }

        return "rank_battle_brother";
    }

    private static string FormatPairs(IReadOnlyList<Tuple<string, string>> pairs)
    {
        return string.Join("\n", pairs.Select(pair => $"{pair.Item1}: {pair.Item2}"));
    }

    private static string FormatLines(IReadOnlyList<string> lines, string emptyText)
    {
        if (lines == null || lines.Count == 0)
        {
            return emptyText;
        }

        return string.Join("\n", lines);
    }
}
