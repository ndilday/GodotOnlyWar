using OnlyWar.Medical.Readiness;
using OnlyWar.Domain;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Application;

/// <summary>
/// The soldier dossier as browser detail cards. Everything it needs — the doctrine, the
/// recruitment program, the date and the sector — is supplied by the caller, so the same builder
/// serves the live chapter screen and any other session.
/// </summary>
public class SoldierDetailBuilder
{
    private readonly SoldierDossierService _dossierService = new();

    public ChapterBrowserDetail Build(
        ISoldier soldier,
        bool includeOpenFullRecordAction,
        SoldierDetailContext context,
        bool includeSquadInTitle = false)
    {
        context ??= SoldierDetailContext.Empty;
        List<ChapterBrowserDetailCard> cards = [];

        if (soldier is PlayerSoldier playerSoldier)
        {
            DutyReadinessEvaluation duty = DutyReadinessService.Evaluate(
                playerSoldier,
                doctrine: context.Doctrine,
                recruitmentProgram: context.RecruitmentProgram);
            SoldierDossier dossier = _dossierService.BuildDossier(
                playerSoldier,
                richTextInjury: false,
                currentDate: context.CurrentDate,
                sector: context.Sector,
                ratingBindings: context.RatingBindings);
            // Left grid, two columns: Posting/Sergeant, Battle/Honors, Injury.
            // Service Record is full-height on the right with its own scroll.
            cards.Add(new ChapterBrowserDetailCard("map_pin", "Posting", "Assignment", FormatPairs(dossier.Data)));
            cards.Add(new ChapterBrowserDetailCard("training", "Sergeant Report", "Recommendation", dossier.SergeantReport));
            cards.Add(new ChapterBrowserDetailCard("threat", "Battle History", "Combat record", FormatPairs(dossier.CombatRecord)));
            cards.Add(new ChapterBrowserDetailCard("award", "Honors", "Awards", FormatLines(dossier.Awards, "No awards recorded."), Scrollable: true));
            cards.Add(new ChapterBrowserDetailCard(
                "medical",
                "Injury Report",
                duty.IsDutyReady ? "Duty-ready report"
                    : duty.ReasonCode == DutyReadinessReasonCode.ChapterInjuryThreshold
                        ? "Withheld by doctrine"
                        : "Recovery report",
                dossier.InjuryReport));
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

        Squad squad = soldier.AssignedSquad;
        DutyReadinessEvaluation readiness = DutyReadinessService.Evaluate(
            soldier,
            doctrine: context.Doctrine,
            recruitmentProgram: context.RecruitmentProgram);
        string status = readiness.IsDutyReady
            ? "Available for duty"
            : readiness.ReasonCode == DutyReadinessReasonCode.ChapterInjuryThreshold
                ? "Withheld by doctrine"
                : "Wounded or impaired";
        string location = SquadLocationFormatter.Format(squad);
        bool canNavigateToLocation = SquadLocationNavigation.Resolve(squad) is not null;

        return new ChapterBrowserDetail(
            GetSoldierIconKey(soldier),
            title,
            canNavigateToLocation ? $"{status} -" : $"{status} - {location}",
            [],
            cards,
            includeOpenFullRecordAction ? "Open Full Record" : null,
            includeOpenFullRecordAction ? "archive" : null,
            canNavigateToLocation ? location : null,
            canNavigateToLocation ? squad.Id : null);
    }

    public static string GetSoldierIconKey(ISoldier soldier)
    {
        if (!soldier.IsCombatEffective)
        {
            return "wounded";
        }
        if (soldier.Template.IsSquadLeader)
        {
            return "rank_sergeant";
        }

        return "rank_battle_brother";
    }

    private static string FormatPairs(IReadOnlyList<ValueTuple<string, string>> pairs)
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
