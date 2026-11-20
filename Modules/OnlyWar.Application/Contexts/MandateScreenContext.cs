using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Campaign.Mandates;
using OnlyWar.Domain;
using OnlyWar.Domain.Mandates;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Medical.Readiness;

namespace OnlyWar.Application;

/// <summary>
/// Reads and commands for mandates, legacy and Chapter Master succession
/// (TDD §6.15). The rules live in OnlyWar.Campaign.Mandates; this context
/// only projects views and validates what the player picked.
/// </summary>
internal sealed class MandateScreenContext
{
    private const int RecentEntryCount = 30;

    private readonly Sector _sector;
    private readonly GameRulesData _rules;
    private readonly Date _currentDate;

    internal MandateScreenContext(Sector sector, GameRulesData rules, Date currentDate)
    {
        _sector = sector ?? throw new ArgumentNullException(nameof(sector));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _currentDate = currentDate ?? throw new ArgumentNullException(nameof(currentDate));
    }

    private PlayerForce Force => _sector.PlayerForce;
    private int Week => _currentDate.GetTotalWeeks();
    private MandateService Service => new(_sector, _rules, _currentDate);

    internal MandateBoardView QueryBoard()
    {
        if (Force == null) return MandateBoardView.Empty;
        ChapterMandates mandates = Force.Mandates;
        Planet home = Force.HomeWorldPlanetId is int homeId ? _sector.Planets.GetValueOrDefault(homeId) : null;
        return new MandateBoardView(
            true,
            BuildActive(mandates.Active),
            Service.ListOptions().Select(BuildOption).ToList(),
            mandates.All
                .Where(mandate => !mandate.IsActive)
                .OrderByDescending(mandate => mandate.ResolvedWeek)
                .ThenByDescending(mandate => mandate.Id)
                .Select(mandate => BuildHistory(mandate, mandates))
                .ToList(),
            mandates.ChapterTotal,
            BuildSeries(mandates),
            BuildMasters(mandates),
            mandates.Ledger
                .OrderByDescending(entry => entry.Week)
                .ThenByDescending(entry => entry.Id)
                .Take(RecentEntryCount)
                .Select(entry => new LegacyEntryView(
                    entry.Week,
                    DateLabel(entry.Week),
                    entry.Amount,
                    SourceLabel(entry.SourceKind),
                    entry.Description,
                    MasterName(mandates, entry.ChapterMasterId)))
                .ToList(),
            home?.Name,
            home == null
                ? null
                : $"Renounce {home.Name} as the Chapter World? The world is no longer the Chapter's "
                    + "home, and the Chapter may swear to establish another."
                    + (mandates.Active?.Family == MandateFamily.SecureChapterWorld
                        ? $" The vow to secure it is broken: legacy {MandateConstants.FailureLegacy(mandates.Active.LegacyValue)}."
                        : string.Empty));
    }

    internal MandateCommandResult Swear(string optionKey)
    {
        if (Force.Mandates.Active != null)
            return new MandateCommandResult(false, "A mandate is already sworn.");
        if (!Service.ListOptions().Any(option => option.Key == optionKey))
            return new MandateCommandResult(false, "That mandate is no longer eligible.");
        Mandate mandate = Service.Swear(optionKey);
        return new MandateCommandResult(true, $"The Chapter has sworn to {LowerFirst(mandate.Title)}.");
    }

    internal MandateCommandResult Abandon()
    {
        Mandate active = Force.Mandates.Active;
        if (active == null) return new MandateCommandResult(false, "No mandate is sworn.");
        Service.Abandon();
        return new MandateCommandResult(true, $"The vow to {LowerFirst(active.Title)} is set aside.");
    }

    internal MandateCommandResult RenounceChapterWorld()
    {
        Planet home = Force.HomeWorldPlanetId is int homeId ? _sector.Planets.GetValueOrDefault(homeId) : null;
        if (home == null) return new MandateCommandResult(false, "The Chapter has no Chapter World.");
        Service.RenounceChapterWorld();
        return new MandateCommandResult(true, $"{home.Name} is no longer the Chapter World.");
    }

    internal ChapterMasterSuccessionView QuerySuccession()
    {
        if (Force == null || _rules.ChapterDoctrine == null)
            return new ChapterMasterSuccessionView(false, null, [], "No campaign is loaded.");
        PlayerSoldier holder = ChapterMasterOffice.FindHolder(Force, _rules);
        if (holder != null)
        {
            return new ChapterMasterSuccessionView(false, holder.Name, [],
                $"{holder.Name} holds the office of Chapter Master.");
        }
        List<ChapterMasterCandidateView> candidates = ChapterMasterOffice.Candidates(Force, _rules)
            .Select(soldier => new ChapterMasterCandidateView(
                soldier.Id,
                soldier.Name,
                soldier.AssignedSquad?.Name ?? "Unassigned",
                DutyReadinessService.Evaluate(
                    soldier,
                    Force.Army.ChapterOperationalDoctrine,
                    Force.RecruitmentProgram).IsDutyReady))
            .ToList();
        return new ChapterMasterSuccessionView(
            true,
            null,
            candidates,
            candidates.Count > 0
                ? "The office of Chapter Master is empty. Promote a Captain to fill it."
                : "The office of Chapter Master is empty, and no Captain lives. Promote a brother "
                    + "to Captain in the muster first.");
    }

    internal ChapterPrompt DescribeChapterMasterPromotion(int soldierId)
    {
        PlayerSoldier soldier = Force?.Army?.PlayerSoldierMap.GetValueOrDefault(soldierId);
        string reason = ChapterMasterOffice.WhyCannotPromote(Force, _rules, soldier);
        if (reason != null) return new ChapterPrompt(ChapterPromptKind.Blocked, "Promotion Blocked", reason);
        return new ChapterPrompt(
            ChapterPromptKind.Confirm,
            "Promote to Chapter Master",
            $"Raise Captain {soldier.Name} to Chapter Master? He stays with {soldier.AssignedSquad?.Name ?? "his squad"} "
                + "until you move him, and his company's Captain post becomes vacant.");
    }

    internal MandateCommandResult PromoteToChapterMaster(int soldierId)
    {
        ChapterPrompt prompt = DescribeChapterMasterPromotion(soldierId);
        if (prompt.Kind != ChapterPromptKind.Confirm) return new MandateCommandResult(false, prompt.Message);
        PlayerSoldier soldier = Force.Army.PlayerSoldierMap[soldierId];
        ChapterMasterOffice.Promote(Force, _rules, soldier, _currentDate);
        return new MandateCommandResult(true, $"{soldier.Name} is now Chapter Master.");
    }

    private ActiveMandateView BuildActive(Mandate mandate)
    {
        if (mandate == null) return null;
        MandateEvaluation evaluation = MandateEvaluator.Evaluate(mandate, _sector, _rules, Week, advance: false);
        int? weeksLeft = mandate.DeadlineWeek - Week;
        int cost = -MandateConstants.FailureLegacy(mandate.LegacyValue);
        string warning = mandate.IsPromisedWorld
            ? $"Give up retaking the Promised World? This has the same result as losing it: the "
                + $"Sector Lord's opinion of the Chapter falls, and the vow is broken (legacy -{cost})."
            : $"Abandon the vow to {LowerFirst(mandate.Title)}? An abandoned vow counts as broken (legacy -{cost})."
                + (mandate.Family == MandateFamily.EstablishChapterWorld
                    ? " The Sector Lord's opinion of the Chapter also falls."
                    : string.Empty);
        return new ActiveMandateView(
            mandate.Id,
            FamilyLabel(mandate.Family, mandate.IsPromisedWorld),
            mandate.Title,
            evaluation.Progress,
            DeadlineLabel(mandate.DeadlineWeek),
            weeksLeft,
            mandate.LegacyValue,
            cost,
            mandate.IsPromisedWorld,
            warning,
            Links(mandate.PlanetIds));
    }

    private MandateOptionView BuildOption(MandateOption option) => new(
        option.Key,
        FamilyLabel(option.Family, false),
        option.Title,
        option.Goal,
        option.DeadlineWeeks,
        $"{option.DeadlineWeeks / MandateConstants.WeeksPerYear} years (by {DateLabel(Week + option.DeadlineWeeks)})",
        option.LegacyValue,
        -MandateConstants.FailureLegacy(option.LegacyValue),
        Links(option.PlanetIds));

    private MandateHistoryView BuildHistory(Mandate mandate, ChapterMandates mandates) => new(
        mandate.Id,
        mandate.Title,
        mandate.State switch
        {
            MandateState.Fulfilled => "Kept",
            MandateState.Abandoned => "Abandoned",
            _ => "Broken"
        },
        DateLabel(mandate.StartWeek),
        mandate.ResolvedWeek is int resolved ? DateLabel(resolved) : string.Empty,
        mandates.Ledger.FirstOrDefault(entry => entry.SourceKey == $"mandate/{mandate.Id}")?.Amount ?? 0);

    private IReadOnlyList<LegacyPointView> BuildSeries(ChapterMandates mandates)
    {
        Dictionary<string, string> markers = mandates.All
            .Where(mandate => mandate.ResolvedWeek.HasValue)
            .ToDictionary(
                mandate => $"mandate/{mandate.Id}",
                mandate => $"{(mandate.State == MandateState.Fulfilled ? "Kept" : "Broken")}: {mandate.Title}");
        List<LegacyPointView> points = [];
        int total = 0;
        foreach (IGrouping<int, LegacyLedgerEntry> week in mandates.Ledger
            .GroupBy(entry => entry.Week)
            .OrderBy(group => group.Key))
        {
            total += week.Sum(entry => entry.Amount);
            string marker = string.Join("; ", week
                .Select(entry => markers.GetValueOrDefault(entry.SourceKey))
                .Where(text => text != null));
            points.Add(new LegacyPointView(week.Key, DateLabel(week.Key), total,
                string.IsNullOrEmpty(marker) ? null : marker));
        }
        return points;
    }

    private IReadOnlyList<LegacyMasterView> BuildMasters(ChapterMandates mandates)
    {
        List<LegacyMasterView> masters = mandates.Tenures
            .GroupBy(tenure => tenure.SoldierId)
            .Select(group =>
            {
                ChapterMasterTenure first = group.First();
                List<LegacyLedgerEntry> entries = mandates.Ledger
                    .Where(entry => entry.ChapterMasterId == group.Key)
                    .ToList();
                string tenure = string.Join(", ", group.Select(item =>
                    $"{DateLabel(item.StartWeek)} to {(item.EndWeek is int end ? DateLabel(end) : "present")}"));
                return new LegacyMasterView(
                    group.Key,
                    first.SoldierName,
                    tenure,
                    entries.Where(entry => entry.Amount > 0).Sum(entry => entry.Amount),
                    entries.Where(entry => entry.Amount < 0).Sum(entry => entry.Amount));
            })
            .ToList();
        List<LegacyLedgerEntry> vacant = mandates.Ledger.Where(entry => entry.ChapterMasterId == null).ToList();
        if (vacant.Count > 0)
        {
            masters.Add(new LegacyMasterView(
                null,
                "No Chapter Master",
                "While the office was empty",
                vacant.Where(entry => entry.Amount > 0).Sum(entry => entry.Amount),
                vacant.Where(entry => entry.Amount < 0).Sum(entry => entry.Amount)));
        }
        return masters;
    }

    private IReadOnlyList<MandateLinkView> Links(IEnumerable<int> planetIds) =>
        planetIds
            .Select(id => _sector.Planets.GetValueOrDefault(id))
            .Where(planet => planet != null)
            .Select(planet => new MandateLinkView(
                $"View {planet.Name}",
                new CampaignNavigationTarget(
                    CampaignNavigationTargetKind.Planet,
                    planet.Id,
                    Fallback: $"View {planet.Name}.",
                    DisplayNameSnapshot: planet.Name)))
            .ToList();

    private string DeadlineLabel(int? deadlineWeek)
    {
        if (deadlineWeek is not int deadline) return "No deadline";
        int left = deadline - Week;
        return left <= 0
            ? "Deadline this turn"
            : $"{left} week{(left == 1 ? string.Empty : "s")} left (by {DateLabel(deadline)})";
    }

    private static string MasterName(ChapterMandates mandates, int? soldierId) =>
        soldierId is int id
            ? mandates.Tenures.LastOrDefault(tenure => tenure.SoldierId == id)?.SoldierName ?? $"Brother {id}"
            : "No Chapter Master";

    private static string DateLabel(int week) => Date.FromTotalWeeks(Math.Max(1, week)).ToString();

    internal static string FamilyLabel(MandateFamily family, bool isPromisedWorld) =>
        isPromisedWorld
            ? "The Promised World"
            : family switch
            {
                MandateFamily.EstablishChapterWorld => "Establish a Chapter World",
                MandateFamily.SecureChapterWorld => "Secure the Chapter World",
                MandateFamily.RebuildCompany => "Rebuild a Company",
                MandateFamily.ProtectStrategicWorlds => "Protect Strategic Worlds",
                MandateFamily.EradicateThreat => "Eradicate a Named Threat",
                MandateFamily.Reputation => "Reputation",
                MandateFamily.SupplyNetwork => "Supply Network",
                _ => family.ToString()
            };

    private static string SourceLabel(LegacySourceKind kind) => kind switch
    {
        LegacySourceKind.MandateFulfilled => "Vow kept",
        LegacySourceKind.MandateFailed => "Vow broken",
        LegacySourceKind.WorldSaved => "World saved",
        LegacySourceKind.WorldLost => "World lost",
        LegacySourceKind.RequestFulfilled => "Petition answered",
        LegacySourceKind.RequestFailed => "Petition failed",
        LegacySourceKind.LargeBattleWon => "Battle won",
        LegacySourceKind.LargeBattleLost => "Battle lost",
        LegacySourceKind.BrotherKilled => "Brother fallen",
        _ => kind.ToString()
    };

    private static string LowerFirst(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToLowerInvariant(text[0]) + text[1..];
}
