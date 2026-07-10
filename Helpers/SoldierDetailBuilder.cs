using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

public class SoldierDetailBuilder
{
    private readonly SoldierDossierService _dossierService = new();

    public ChapterBrowserDetail Build(ISoldier soldier, bool includeOpenFullRecordAction)
    {
        List<ChapterBrowserDetailCard> cards = [];

        if (soldier is PlayerSoldier playerSoldier)
        {
            SoldierDossier dossier = _dossierService.BuildDossier(
                playerSoldier,
                richTextInjury: false,
                currentDate: GameDataSingleton.Instance.Date,
                sector: GameDataSingleton.Instance.Sector);
            // Top row
            cards.Add(new ChapterBrowserDetailCard("map_pin", "Posting", "Assignment", FormatPairs(dossier.Data)));
            cards.Add(new ChapterBrowserDetailCard("award", "Honors", "Awards", FormatLines(dossier.Awards, "No awards recorded.", 5)));
            cards.Add(new ChapterBrowserDetailCard("threat", "Battle History", "Combat record", FormatPairs(dossier.CombatRecord)));
            // Second row
            cards.Add(new ChapterBrowserDetailCard("medical", "Injury Report", playerSoldier.CanFight ? "Fit report" : "Recovery report", dossier.InjuryReport));
            cards.Add(new ChapterBrowserDetailCard("training", "Sergeant Report", "Recommendation", dossier.SergeantReport));
            cards.Add(new ChapterBrowserDetailCard("archive", "Service Record", "Recent entries", FormatLines(dossier.History, "No history recorded.", 5)));
        }
        else
        {
            cards.Add(new ChapterBrowserDetailCard("archive", "Record", "Chronicle", "Detailed battle history is only available for player soldiers."));
        }

        return new ChapterBrowserDetail(
            GetSoldierIconKey(soldier),
            $"{soldier.Template.Name} {soldier.Name}",
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

    private static string FormatLines(IReadOnlyList<string> lines, string emptyText, int maxLines)
    {
        if (lines == null || lines.Count == 0)
        {
            return emptyText;
        }

        return string.Join("\n", lines.Reverse().Take(maxLines).Reverse());
    }
}
