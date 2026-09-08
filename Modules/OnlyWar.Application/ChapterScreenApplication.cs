using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Readiness;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;

namespace OnlyWar.Application;

public sealed class ChapterScreenApplication : CampaignScreenApplication,
    IChapterScreenApplication
{
    private readonly SoldierTransferService _transferService = new();
    private readonly SoldierDetailBuilder _soldierDetailBuilder = new();
    private readonly SoldierFilterService _filterService = new();
    private readonly SquadRowViewModelBuilder _chapterRowBuilder = new();

    public ChapterScreenApplication(CampaignApplicationContext context) : base(context) { }

    public bool HasChapter => TryGetChapter() != null;

    public ChapterBrowserView QueryChapterBrowser(ChapterBrowserQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        PlayerForce force = ActiveSession?.Sector.PlayerForce;

        if (query.HistoricalSoldierId.HasValue
            && force?.Army?.FallenBrothers.TryGetValue(
                query.HistoricalSoldierId.Value, out PlayerSoldier fallen) == true)
        {
            return BuildHistoricalSoldierView(force, fallen);
        }

        Unit chapter = TryGetChapter();
        if (chapter == null) return BuildNoChapterView();

        IReadOnlyList<SoldierFilterCondition> filter = query.Filter ?? [];
        ChapterBrowserView view = filter.Count > 0
            ? BuildFilterResultsView(chapter, force, query, filter)
            : query.SoldierId.HasValue
                ? BuildSoldierLevelView(chapter, force, query)
                : query.SquadId.HasValue
                    ? BuildSquadLevelView(chapter, force, query)
                    : query.CompanyId.HasValue
                        ? BuildCompanyLevelView(chapter, force, query)
                        : BuildChapterLevelView(chapter, force, query);

        return view with { Breadcrumbs = BuildBreadcrumbs(chapter, query) };
    }

    public ChapterFilterOptions QueryFilterOptions(ChapterBrowserQuery query)
    {
        Unit chapter = TryGetChapter();
        if (chapter == null) return new ChapterFilterOptions([], []);

        List<ISoldier> scope = GetScopeMembers(chapter, query).ToList();
        GameRulesData rules = ActiveSession.Rules;
        return new ChapterFilterOptions(
            _filterService.GetAvailableRoles(scope),
            _filterService.GetAvailableHonors(
                scope, rules.RatingAwardTiers, rules.AwardCatalog, rules.RatingConsumers));
    }

    // The breadcrumb path models companies as direct children of the chapter, so map a squad back
    // to the company that owns it (a company may nest the squad in a sub-unit, hence GetAllSquads).
    // Returns null for a chapter-level command squad that hangs directly off the order of battle.
    public int? FindCompanyForSquad(int squadId)
    {
        Unit chapter = TryGetChapter();
        Squad squad = chapter?.GetAllSquads().FirstOrDefault(candidate => candidate.Id == squadId);
        if (squad == null || chapter.Squads.Contains(squad)) return null;

        foreach (Unit company in chapter.ChildUnits)
        {
            if (company.GetAllSquads().Contains(squad)) return company.Id;
        }
        return null;
    }

    public bool CanNavigateToSquad(int squadId) =>
        SquadLocationNavigation.Resolve(
            TryGetChapter()?.GetAllSquads().FirstOrDefault(squad => squad.Id == squadId))
            is not null;

    public ChapterPrompt DescribeTransfer(int soldierId, int optionIndex)
    {
        if (FindChapterSoldier(soldierId) is not PlayerSoldier soldier) return ChapterPrompt.None;
        if (ResolveTransferOption(soldier, optionIndex) is not SoldierTransferOption option)
        {
            return ChapterPrompt.None;
        }

        PlayerForce force = ActiveSession.Sector.PlayerForce;
        force.Army.PopulateSquadMap();
        if (_transferService.WouldExceedShipCapacity(soldier, option, force.Army.SquadMap))
        {
            string transferTarget = _transferService.FormatBlockedTransferTarget(
                option, force.Army.SquadMap);
            return new ChapterPrompt(
                ChapterPromptKind.Blocked,
                "Transfer Blocked",
                $"{transferTarget} has no room aboard its ship. Free up space before "
                    + $"transferring {soldier.Name} there.");
        }

        if (!SoldierTransferService.RequiresBlackCarapace(soldier, option))
        {
            return new ChapterPrompt(
                ChapterPromptKind.Confirm,
                "Confirm Transfer",
                $"Transfer {soldier.Template.Name} {soldier.Name} to {option.DisplayName}?");
        }

        if (option.IsNewSquad
            || option.SoldierTemplate != ActiveSession.Rules.ChapterDoctrine.DevastatorMarine)
        {
            return new ChapterPrompt(
                ChapterPromptKind.Blocked,
                "Promotion Blocked",
                "A campaign-recruited neophyte must receive the Black Carapace "
                    + "before changing roles, and his first Battle-Brother posting "
                    + "must be as a Devastator Marine.");
        }

        BlackCarapacePlanResult plan = new RecruitmentPromotionService(ActiveSession)
            .EvaluateBlackCarapace(soldier.Id, option.SquadId);
        if (!plan.Succeeded)
        {
            return new ChapterPrompt(ChapterPromptKind.Blocked, "Promotion Blocked", plan.Message);
        }

        return new ChapterPrompt(
            ChapterPromptKind.Confirm,
            "Confirm Black Carapace Surgery",
            $"Commit {soldier.Name} to a one-week Black Carapace procedure? "
                + $"{plan.ApothecaryName} will perform the surgery. His genetic "
                + $"compatibility is {plan.GeneticCompatibility:P0}; failure is fatal. "
                + $"If he survives, he will join {option.DisplayName}.");
    }

    public ChapterTransferResult ConfirmTransfer(
        Guid sessionToken, int soldierId, int optionIndex, IReadOnlyList<int> contextSoldierIds)
    {
        if (ActiveSession == null || sessionToken != SessionToken)
        {
            return new ChapterTransferResult(false, ChapterPrompt.None, false, false, null, null);
        }
        if (FindChapterSoldier(soldierId) is not PlayerSoldier soldier
            || ResolveTransferOption(soldier, optionIndex) is not SoldierTransferOption option)
        {
            return new ChapterTransferResult(false, ChapterPrompt.None, false, false, null, null);
        }

        PlayerForce force = ActiveSession.Sector.PlayerForce;
        if (SoldierTransferService.RequiresBlackCarapace(soldier, option))
        {
            RecruitmentPromotionResult promotion = new RecruitmentPromotionService(ActiveSession)
                .ScheduleBlackCarapace(soldier.Id, option.SquadId);
            return new ChapterTransferResult(
                promotion.Succeeded,
                new ChapterPrompt(
                    ChapterPromptKind.Blocked,
                    promotion.Succeeded ? "Surgery Scheduled" : "Promotion Blocked",
                    promotion.Message),
                DidTransfer: false,
                OriginSquadRemoved: false,
                null,
                null);
        }

        int originSquadId = soldier.AssignedSquad.Id;
        force.Army.PopulateSquadMap();
        bool didTransfer = _transferService.ApplyTransfer(
            soldier, option, force.Army.SquadMap, ActiveSession.CurrentDate);
        if (!didTransfer)
        {
            return new ChapterTransferResult(false, ChapterPrompt.None, false, false, null, null);
        }

        // Moving the last member out empties (and disbands) the origin squad — e.g. the final
        // scout leaving a scout squad. Report that so a browser standing on the vanished squad
        // can follow the soldier to his new home instead of rendering a stale path.
        bool originSquadRemoved =
            !TryGetChapter().GetAllSquads().Any(squad => squad.Id == originSquadId);
        Squad newSquad = soldier.AssignedSquad;
        return new ChapterTransferResult(
            true,
            ChapterPrompt.None,
            DidTransfer: true,
            originSquadRemoved,
            newSquad == null ? null : FindCompanyForSquad(newSquad.Id),
            newSquad?.Id);
    }

    // "Recall from operation" on an assigned brother's detail card. A brother committed to an
    // operation is serving with someone else's force; the recall returns him to his own squad.
    public ChapterPrompt DescribeRecall(int soldierId)
    {
        if (FindChapterSoldier(soldierId) is not PlayerSoldier soldier
            || soldier.CurrentOrder == null)
        {
            return ChapterPrompt.None;
        }

        return new ChapterPrompt(
            ChapterPromptKind.Confirm,
            "Confirm Recall",
            $"Recall {soldier.Template.Name} {soldier.Name} from the operation in "
                + $"{soldier.CurrentOrder.Mission?.RegionFaction?.Region?.Name ?? "the field"}? "
                + $"He rejoins {soldier.AssignedSquad?.Name} immediately.");
    }

    public ChapterTransferResult ConfirmRecall(Guid sessionToken, int soldierId)
    {
        if (ActiveSession == null || sessionToken != SessionToken
            || FindChapterSoldier(soldierId) is not PlayerSoldier soldier
            || soldier.CurrentOrder == null)
        {
            return new ChapterTransferResult(false, ChapterPrompt.None, false, false, null, null);
        }

        Helpers.Orders.OrderForceService.RemoveCharacter(soldier);
        return new ChapterTransferResult(
            true, ChapterPrompt.None, false, false, null, soldier.AssignedSquad?.Id);
    }

    private ChapterBrowserView BuildNoChapterView() => new(
        false,
        [new ChapterBreadcrumbItem(ChapterBrowserLevel.Chapter, "Chapter", "chapter")],
        "Companies",
        [],
        new ChapterBrowserDetail(
            "chapter",
            "No Chapter Data",
            "Chapter data will appear here once a game is loaded.",
            [
                new ChapterBrowserMetric("0", "Soldiers"),
                new ChapterBrowserMetric("0", "Squads"),
                new ChapterBrowserMetric("0", "Wounded")
            ],
            [
                new ChapterBrowserDetailCard("archive", "Awaiting Game Data", "No active chapter",
                    "Open this screen through the main game flow to browse companies, squads, and soldiers.")
            ]),
        null, null, [], [], null);

    private ChapterBrowserView BuildHistoricalSoldierView(PlayerForce force, PlayerSoldier soldier)
    {
        List<ChapterBrowserMenuItem> fallenBrothers = force.Army.FallenBrothers.Values
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => new ChapterBrowserMenuItem(
                ChapterBrowserLevel.Soldier,
                candidate.Id,
                SoldierDetailBuilder.GetSoldierIconKey(candidate),
                $"{candidate.Template.Name} {candidate.Name}",
                "Fallen — preserved dossier",
                true,
                candidate.Id == soldier.Id,
                "i"))
            .ToList();

        ChapterBrowserDetail baseDetail = _soldierDetailBuilder.Build(
            soldier, false, BuildSoldierDetailContext(force), includeSquadInTitle: true);
        ChapterBrowserDetail detail = baseDetail with
        {
            Subtitle = "Fallen — preserved dossier",
            Cards =
            [
                new ChapterBrowserDetailCard(
                    "archive",
                    "Historical Dossier",
                    "No active posting",
                    "This brother is no longer part of the active order of battle. His name, service record, and campaign history remain preserved here."),
                .. baseDetail.Cards
            ]
        };

        return new ChapterBrowserView(
            true,
            [new ChapterBreadcrumbItem(ChapterBrowserLevel.Chapter, "Chapter", "chapter")],
            "Fallen Brothers", fallenBrothers, detail,
            soldier.Id, null, [], [], soldier.Id);
    }

    private ChapterBrowserView BuildChapterLevelView(
        Unit chapter, PlayerForce force, ChapterBrowserQuery query)
    {
        Unit selectedCompany = ResolveSelectedCompany(chapter, query);
        Squad selectedSquad = ResolveSelectedSquad(chapter, query);
        List<Squad> orderedSquads = OrderSquads(chapter.Squads).ToList();
        if (selectedCompany == null && selectedSquad == null)
        {
            selectedSquad = orderedSquads.FirstOrDefault();
            selectedCompany = selectedSquad == null ? chapter.ChildUnits.FirstOrDefault() : null;
        }

        List<ChapterBrowserMenuItem> items = orderedSquads
            .Select(squad => new ChapterBrowserMenuItem(
                ChapterBrowserLevel.Squad,
                squad.Id,
                GetSquadIconKey(squad),
                squad.Name,
                $"{squad.SquadTemplate.Name} - {squad.Members.Count} soldiers",
                true,
                selectedSquad?.Id == squad.Id,
                ">",
                CanNavigate: SquadLocationNavigation.Resolve(squad) is not null,
                SquadRow: BuildChapterSquadRow(force, squad, selectedSquad?.Id == squad.Id)))
            .ToList();

        items.AddRange(chapter.ChildUnits
            .Select(company => new ChapterBrowserMenuItem(
                ChapterBrowserLevel.Company,
                company.Id,
                GetCompanyIconKey(company),
                company.Name,
                $"{FormatCompanySquadCount(company)} squads - {company.GetAllMembers().Count()} soldiers",
                true,
                selectedCompany?.Id == company.Id,
                ">")));

        return new ChapterBrowserView(
            true, [], "Chapter Command", items,
            BuildChapterDetail(chapter, selectedCompany, selectedSquad),
            null, null, [], [], null);
    }

    private ChapterBrowserView BuildCompanyLevelView(
        Unit chapter, PlayerForce force, ChapterBrowserQuery query)
    {
        Unit company = chapter.ChildUnits.FirstOrDefault(
            candidate => candidate.Id == query.CompanyId.Value);
        if (company == null) return BuildChapterLevelView(chapter, force, query with { CompanyId = null });

        List<Squad> orderedSquads = OrderSquads(company.Squads).ToList();
        Squad selectedSquad = ResolveSelectedSquad(chapter, query) ?? orderedSquads.FirstOrDefault();

        List<ChapterBrowserMenuItem> squads = orderedSquads
            .Select(squad => new ChapterBrowserMenuItem(
                ChapterBrowserLevel.Squad,
                squad.Id,
                GetSquadIconKey(squad),
                squad.Name,
                $"{squad.SquadTemplate.Name} - {squad.Members.Count} soldiers",
                true,
                selectedSquad?.Id == squad.Id,
                Location: SquadLocationFormatter.Format(squad),
                CanNavigate: SquadLocationNavigation.Resolve(squad) is not null,
                SquadRow: BuildChapterSquadRow(force, squad, selectedSquad?.Id == squad.Id)))
            .ToList();

        return new ChapterBrowserView(
            true, [], $"{company.Name} Squads", squads,
            BuildCompanyDetail(company, selectedSquad),
            null, null, [], [], null);
    }

    private ChapterBrowserView BuildSquadLevelView(
        Unit chapter, PlayerForce force, ChapterBrowserQuery query)
    {
        Squad squad = chapter.GetAllSquads().FirstOrDefault(
            candidate => candidate.Id == query.SquadId.Value);
        if (squad == null)
        {
            return BuildChapterLevelView(chapter, force, query with { SquadId = null });
        }

        List<ISoldier> orderedMembers = OrderByRankAndTenure(squad.Members).ToList();
        ISoldier selected = ResolveSelectedSoldier(chapter, query) ?? orderedMembers.FirstOrDefault();

        List<ChapterBrowserMenuItem> soldiers = orderedMembers
            .Select(soldier => BuildSoldierMenuItem(force, soldier, selected?.Id == soldier.Id))
            .ToList();

        // A soldier is a leaf, so entering a squad shows the auto-selected top member's own
        // detail (with the transfer control) rather than a squad overview, keeping squad entry
        // consistent with clicking any other member. Empty squads fall back to the overview.
        if (selected == null)
        {
            return new ChapterBrowserView(
                true, [], "Battle Brothers", soldiers, BuildSquadDetail(squad, null),
                null, null, [], [], null);
        }

        return BuildSoldierDetailView(
            force, soldiers, "Battle Brothers", selected,
            orderedMembers.Select(member => member.Id).ToList());
    }

    private ChapterBrowserView BuildSoldierLevelView(
        Unit chapter, PlayerForce force, ChapterBrowserQuery query)
    {
        ISoldier soldier = chapter.GetAllMembers()
            .FirstOrDefault(candidate => candidate.Id == query.SoldierId.Value);
        if (soldier?.AssignedSquad == null)
        {
            return BuildChapterLevelView(chapter, force, query with { SoldierId = null });
        }

        List<ISoldier> members = OrderByRankAndTenure(soldier.AssignedSquad.Members).ToList();
        List<ChapterBrowserMenuItem> soldiers = members
            .Select(member => BuildSoldierMenuItem(force, member, member.Id == soldier.Id))
            .ToList();

        return BuildSoldierDetailView(
            force, soldiers, "Battle Brothers", soldier,
            members.Select(member => member.Id).ToList());
    }

    // Renders the active filter as a flat, drillable soldier list scoped to the current
    // browse level. Selecting a result previews it; drilling (or navigating) exits filtering.
    private ChapterBrowserView BuildFilterResultsView(
        Unit chapter,
        PlayerForce force,
        ChapterBrowserQuery query,
        IReadOnlyList<SoldierFilterCondition> filter)
    {
        List<ISoldier> results = FilteredSoldiers(chapter, query, filter);
        int? selectedId = query.SelectedLevel == ChapterBrowserLevel.Soldier
            ? query.SelectedId
            : null;
        ISoldier selected = results.FirstOrDefault(soldier => soldier.Id == selectedId)
            ?? results.FirstOrDefault();

        List<ChapterBrowserMenuItem> items = results
            .Select(soldier => BuildSoldierMenuItem(force, soldier, selected?.Id == soldier.Id))
            .ToList();
        string title = $"Filter Results ({results.Count})";

        if (selected == null)
        {
            return new ChapterBrowserView(
                true, [], title, items,
                new ChapterBrowserDetail(
                    "archive",
                    "No Matches",
                    "No battle brothers at this level match the current filter.",
                    [],
                    [
                        new ChapterBrowserDetailCard("archive", "Adjust Filter", "No results",
                            "Reopen the Filter button to change the conditions, or Clear to resume browsing.")
                    ]),
                null, null, [], [], null);
        }

        return BuildSoldierDetailView(
            force, items, title, selected, results.Select(soldier => soldier.Id).ToList());
    }

    private ChapterBrowserView BuildSoldierDetailView(
        PlayerForce force,
        IReadOnlyList<ChapterBrowserMenuItem> menu,
        string menuTitle,
        ISoldier soldier,
        IReadOnlyList<int> contextSoldierIds)
    {
        ChapterBrowserDetail detail = _soldierDetailBuilder.Build(
            soldier, false, BuildSoldierDetailContext(force), includeSquadInTitle: true);

        // A brother assigned to an operation is in the field with someone else's force. Surface
        // that, offer the recall, and withhold the transfer options — ApplyTransfer refuses him
        // anyway (§3.4), so offering them would only produce a silent no-op.
        if (soldier is PlayerSoldier attached && attached.CurrentOrder != null)
        {
            string where = attached.CurrentOrder.Mission?.RegionFaction?.Region?.Name
                ?? "an ongoing operation";
            detail = detail with
            {
                Cards =
                [
                    new ChapterBrowserDetailCard(
                        "target",
                        "Assigned to Operation",
                        where,
                        $"{soldier.Name} is away from {attached.AssignedSquad?.Name} and "
                        + $"serving with the force committed to {where}. He returns when the "
                        + "operation ends, or on recall. Transfers are unavailable while he is "
                        + "in the field."),
                    .. detail.Cards
                ],
                PrimaryActionText = "Recall from operation",
                PrimaryActionIconKey = "archive"
            };
            return new ChapterBrowserView(
                true, [], menuTitle, menu, detail, soldier.Id, soldier.AssignedSquad?.Id,
                [], contextSoldierIds, soldier.Id);
        }

        IReadOnlyList<string> transferOptions = soldier is PlayerSoldier playerSoldier
            ? _transferService
                .GetTransferOptions(force.Army.OrderOfBattle, playerSoldier)
                .Select(option => option.DisplayName)
                .ToList()
            : [];

        return new ChapterBrowserView(
            true, [], menuTitle, menu, detail, soldier.Id, soldier.AssignedSquad?.Id,
            transferOptions, contextSoldierIds, soldier.Id);
    }

    private ChapterBrowserMenuItem BuildSoldierMenuItem(
        PlayerForce force, ISoldier soldier, bool selected) =>
        new(ChapterBrowserLevel.Soldier,
            soldier.Id,
            SoldierDetailBuilder.GetSoldierIconKey(soldier),
            $"{soldier.Template.Name} {soldier.Name}",
            DutyStatus(force, soldier),
            true,
            selected,
            "i");

    private SquadRowViewModel BuildChapterSquadRow(PlayerForce force, Squad squad, bool selected) =>
        _chapterRowBuilder.Build(
            squad,
            new SquadRowContext(
                SquadRowContextKind.Chapter,
                SquadRowAction.Inspect,
                isSelected: selected,
                isSelectable: true,
                isEnabled: true),
            force?.RecruitmentProgram,
            force?.Army?.OperationalDoctrine);

    private SoldierDetailContext BuildSoldierDetailContext(PlayerForce force) => new(
        force?.Army?.ChapterOperationalDoctrine,
        force?.RecruitmentProgram,
        ActiveSession?.CurrentDate,
        ActiveSession?.Sector,
        ActiveSession?.Rules?.RatingConsumers);

    private List<ISoldier> FilteredSoldiers(
        Unit chapter, ChapterBrowserQuery query, IReadOnlyList<SoldierFilterCondition> filter) =>
        OrderFilteredSoldiers(_filterService.Apply(
            GetScopeMembers(chapter, query),
            filter.ToList(),
            ActiveSession.CurrentDate,
            ActiveSession.Rules.RatingConsumers))
            .ToList();

    // Soldiers the filter searches, bound to the current breadcrumb level. Presented in the
    // same seniority order the rosters use before the filtered result list applies its own
    // rank/subrank/surname presentation order.
    private static IEnumerable<ISoldier> GetScopeMembers(Unit chapter, ChapterBrowserQuery query)
    {
        IEnumerable<ISoldier> members;
        if (query.SoldierId.HasValue)
        {
            members = chapter.GetAllMembers()
                .FirstOrDefault(soldier => soldier.Id == query.SoldierId.Value)
                ?.AssignedSquad?.Members ?? chapter.GetAllMembers();
        }
        else if (query.SquadId.HasValue)
        {
            members = chapter.GetAllSquads()
                .FirstOrDefault(squad => squad.Id == query.SquadId.Value)
                ?.Members ?? chapter.GetAllMembers();
        }
        else if (query.CompanyId.HasValue)
        {
            members = chapter.ChildUnits
                .FirstOrDefault(company => company.Id == query.CompanyId.Value)
                ?.GetAllMembers() ?? chapter.GetAllMembers();
        }
        else
        {
            members = chapter.GetAllMembers();
        }
        return OrderByRankAndTenure(members);
    }

    // Filter results span squads and companies, so tenure is less useful than a predictable
    // alphabetical tie-break. Keep role seniority first, then alphabetize equal-ranked brothers
    // by surname regardless of the order in which their squads appear in the chapter.
    internal static IEnumerable<ISoldier> OrderFilteredSoldiers(IEnumerable<ISoldier> soldiers) =>
        soldiers
            .OrderByDescending(soldier => soldier.Template.Rank)
            .ThenByDescending(soldier => soldier.Template.Subrank)
            .ThenBy(GetSurname, StringComparer.OrdinalIgnoreCase)
            .ThenBy(soldier => soldier.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(soldier => soldier.Id);

    private static string GetSurname(ISoldier soldier)
    {
        string name = soldier?.Name?.Trim() ?? "";
        int separatorIndex = name.LastIndexOf(' ');
        return separatorIndex >= 0 ? name[(separatorIndex + 1)..] : name;
    }

    internal static IEnumerable<Squad> OrderSquads(IEnumerable<Squad> squads) =>
        squads
            .OrderBy(ForceOrdering.SquadTypeOrder)
            .ThenBy(squad => squad.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(squad => squad.Id);

    // Rosters and filter results are presented rank-first (most senior at the top), then by
    // time in rank so the longest-tenured brother within a rank leads. Subrank breaks the tie
    // for roles that share a Rank, so a squad leader always sorts above his brothers.
    private static IEnumerable<ISoldier> OrderByRankAndTenure(IEnumerable<ISoldier> soldiers) =>
        SoldierSeniority.OrderBySeniority(soldiers);

    private Unit TryGetChapter() => ActiveSession?.Sector.PlayerForce?.Army?.OrderOfBattle;

    private ISoldier FindChapterSoldier(int soldierId)
    {
        Unit chapter = TryGetChapter();
        return chapter?.GetAllMembers().FirstOrDefault(soldier => soldier.Id == soldierId)
            ?? ActiveSession?.Sector.PlayerForce?.Army?.FallenBrothers
                .GetValueOrDefault(soldierId);
    }

    private SoldierTransferOption ResolveTransferOption(PlayerSoldier soldier, int optionIndex)
    {
        List<SoldierTransferOption> options = _transferService.GetTransferOptions(
            ActiveSession.Sector.PlayerForce.Army.OrderOfBattle, soldier);
        return optionIndex < 0 || optionIndex >= options.Count ? null : options[optionIndex];
    }

    private static Unit ResolveSelectedCompany(Unit chapter, ChapterBrowserQuery query) =>
        query.SelectedLevel == ChapterBrowserLevel.Company && query.SelectedId.HasValue
            ? chapter.ChildUnits.FirstOrDefault(company => company.Id == query.SelectedId.Value)
            : null;

    private static Squad ResolveSelectedSquad(Unit chapter, ChapterBrowserQuery query) =>
        query.SelectedLevel == ChapterBrowserLevel.Squad && query.SelectedId.HasValue
            ? chapter.GetAllSquads().FirstOrDefault(squad => squad.Id == query.SelectedId.Value)
            : null;

    private static ISoldier ResolveSelectedSoldier(Unit chapter, ChapterBrowserQuery query) =>
        query.SelectedLevel == ChapterBrowserLevel.Soldier && query.SelectedId.HasValue
            ? chapter.GetAllMembers().FirstOrDefault(soldier => soldier.Id == query.SelectedId.Value)
            : null;

    private static IReadOnlyList<ChapterBreadcrumbItem> BuildBreadcrumbs(
        Unit chapter, ChapterBrowserQuery query)
    {
        List<ChapterBreadcrumbItem> breadcrumbs =
        [
            new ChapterBreadcrumbItem(ChapterBrowserLevel.Chapter, "Chapter", "chapter")
        ];

        if (query.CompanyId.HasValue
            && chapter.ChildUnits.FirstOrDefault(unit => unit.Id == query.CompanyId.Value)
                is Unit company)
        {
            breadcrumbs.Add(new ChapterBreadcrumbItem(
                ChapterBrowserLevel.Company, company.Name, GetCompanyIconKey(company)));
        }

        if (query.SquadId.HasValue
            && chapter.GetAllSquads().FirstOrDefault(candidate => candidate.Id == query.SquadId.Value)
                is Squad squad)
        {
            breadcrumbs.Add(new ChapterBreadcrumbItem(
                ChapterBrowserLevel.Squad, squad.Name, GetSquadIconKey(squad)));
        }

        if (query.SoldierId.HasValue
            && chapter.GetAllMembers().FirstOrDefault(
                candidate => candidate.Id == query.SoldierId.Value) is ISoldier soldier)
        {
            breadcrumbs.Add(new ChapterBreadcrumbItem(
                ChapterBrowserLevel.Soldier,
                soldier.Name,
                SoldierDetailBuilder.GetSoldierIconKey(soldier)));
        }

        return breadcrumbs;
    }

    private static ChapterBrowserDetail BuildChapterDetail(
        Unit chapter, Unit selectedCompany, Squad selectedSquad)
    {
        int soldierCount = chapter.GetAllMembers().Count();
        int squadCount = chapter.GetAllSquads().Count();
        int woundedCount = chapter.GetAllMembers().Count(soldier => !soldier.IsCombatEffective);

        // Scouts are neophytes, not yet full battle brothers, so report them separately
        // from the battle-brother line. Their sergeants are full marines leading them.
        List<Squad> scoutSquads = chapter.GetAllSquads()
            .Where(squad => (squad.SquadTemplate.SquadType & SquadTypes.Scout) > 0)
            .ToList();
        int neophyteCount = scoutSquads.Sum(
            squad => squad.Members.Count(member => !member.Template.IsSquadLeader));
        int scoutSergeantCount = scoutSquads.Count(squad => squad.SquadLeader != null);
        int battleBrotherCount = soldierCount - neophyteCount - scoutSergeantCount;
        int battleBrotherSquadCount = squadCount - scoutSquads.Count;

        string strengthText =
            $"{battleBrotherCount} battle brothers across {battleBrotherSquadCount} squads";
        strengthText += scoutSergeantCount > 0
            ? $", and {scoutSergeantCount} Scout Sergeants training {neophyteCount} Neophytes."
            : ".";

        List<ChapterBrowserDetailCard> cards =
        [
            new ChapterBrowserDetailCard("chapter", "Chapter Strength", chapter.Name, strengthText),
            new ChapterBrowserDetailCard("medical", "Recovery", "Apothecarium demand", $"{woundedCount} soldiers are wounded or impaired.")
        ];

        if (selectedCompany != null)
        {
            cards.Insert(0, new ChapterBrowserDetailCard(
                GetCompanyIconKey(selectedCompany),
                $"Selected: {selectedCompany.Name}",
                selectedCompany.UnitTemplate.Name,
                $"{FormatCompanySquadCount(selectedCompany)} squads, {selectedCompany.GetAllMembers().Count()} soldiers."));
        }

        if (selectedSquad != null)
        {
            cards.Insert(0, new ChapterBrowserDetailCard(
                GetSquadIconKey(selectedSquad),
                $"Selected: {selectedSquad.Name}",
                selectedSquad.SquadTemplate.Name,
                $"{selectedSquad.Members.Count} soldiers. Drill in to inspect individual battle brothers."));
        }

        return new ChapterBrowserDetail(
            "chapter",
            chapter.Name,
            "Chapter-level overview. Select command squads or companies for a preview; drill into either to manage their roster.",
            [
                new ChapterBrowserMetric(soldierCount.ToString(), "Soldiers"),
                new ChapterBrowserMetric(squadCount.ToString(), "Squads"),
                new ChapterBrowserMetric(woundedCount.ToString(), "Wounded")
            ],
            cards);
    }

    private static ChapterBrowserDetail BuildCompanyDetail(Unit company, Squad selectedSquad)
    {
        int soldierCount = company.GetAllMembers().Count();
        int woundedCount = company.GetAllMembers().Count(soldier => !soldier.IsCombatEffective);

        List<ChapterBrowserDetailCard> cards =
        [
            new ChapterBrowserDetailCard(GetCompanyIconKey(company), "Company Strength", company.UnitTemplate.Name, $"{soldierCount} soldiers across {FormatCompanySquadCount(company)} squads."),
            new ChapterBrowserDetailCard("medical", "Company Recovery", "Readiness impact", $"{woundedCount} soldiers are wounded or impaired."),
            new ChapterBrowserDetailCard("archive", "Company Record", "Chronicle", "Company history and honors can live here as the detail renderer grows.")
        ];

        if (selectedSquad != null)
        {
            cards.Insert(0, new ChapterBrowserDetailCard(
                GetSquadIconKey(selectedSquad),
                $"Selected: {selectedSquad.Name}",
                selectedSquad.SquadTemplate.Name,
                $"{selectedSquad.Members.Count} soldiers. Drill in to inspect individual battle brothers."));
        }

        return new ChapterBrowserDetail(
            GetCompanyIconKey(company),
            company.Name,
            "Company-level overview. Select a squad for a preview; drill into it to manage soldiers.",
            [
                new ChapterBrowserMetric(soldierCount.ToString(), "Soldiers"),
                new ChapterBrowserMetric(FormatCompanySquadCount(company), "Squads"),
                new ChapterBrowserMetric(woundedCount.ToString(), "Wounded")
            ],
            cards);
    }

    private ChapterBrowserDetail BuildSquadDetail(Squad squad, ISoldier selectedSoldier)
    {
        int woundedCount = squad.Members.Count(soldier => !soldier.IsCombatEffective);
        // Headcount stays whole (attachment never touches Squad.Members); this is the
        // "available right now" counterpart the roster needs.
        int assignedCount = squad.Members
            .OfType<PlayerSoldier>()
            .Count(soldier => soldier.CurrentOrder != null);
        string assignedNote = assignedCount == 0
            ? ""
            : $" {assignedCount} assigned to operations elsewhere.";

        List<ChapterBrowserDetailCard> cards =
        [
            new ChapterBrowserDetailCard(GetSquadIconKey(squad), "Squad Composition", squad.SquadTemplate.Name, $"{squad.Members.Count} battle brothers assigned.{assignedNote}"),
            new ChapterBrowserDetailCard("medical", "Casualties", "Current condition", $"{woundedCount} soldiers are wounded or impaired."),
            new ChapterBrowserDetailCard("archive", "Squad Record", "Chronicle", "Squad history, honors, and mission record can expand here.")
        ];

        if (selectedSoldier != null)
        {
            cards.Insert(0, new ChapterBrowserDetailCard(
                SoldierDetailBuilder.GetSoldierIconKey(selectedSoldier),
                $"Selected: {selectedSoldier.Template.Name} {selectedSoldier.Name}",
                DutyStatus(ActiveSession?.Sector.PlayerForce, selectedSoldier),
                "Select a soldier for preview; use the detail button to open the existing soldier display flow."));
        }

        return new ChapterBrowserDetail(
            GetSquadIconKey(squad),
            squad.Name,
            $"{SquadLocationFormatter.Format(squad)}. Select individual soldiers to inspect their status.",
            [
                new ChapterBrowserMetric(squad.Members.Count.ToString(), "Soldiers"),
                new ChapterBrowserMetric(woundedCount.ToString(), "Wounded"),
                new ChapterBrowserMetric(squad.SquadTemplate.BattleValue.ToString(), "Battle Value")
            ],
            cards);
    }

    // Companies always have a single HQ squad plus a variable number of line
    // squads; surface that as "HQ + N" so the HQ isn't conflated with line strength.
    private static string FormatCompanySquadCount(Unit company)
    {
        int nonHqSquads = company.Squads.Count(
            squad => (squad.SquadTemplate.SquadType & SquadTypes.HQ) == 0);
        bool hasHqSquad = company.Squads.Any(
            squad => (squad.SquadTemplate.SquadType & SquadTypes.HQ) != 0);
        return hasHqSquad ? $"HQ + {nonHqSquads}" : nonHqSquads.ToString();
    }

    private static string DutyStatus(PlayerForce force, ISoldier soldier)
    {
        DutyReadinessEvaluation evaluation = DutyReadinessService.Evaluate(
            soldier,
            doctrine: force?.Army?.ChapterOperationalDoctrine,
            recruitmentProgram: force?.RecruitmentProgram);
        return evaluation.IsDutyReady
            ? "Duty-ready"
            : evaluation.ReasonCode == DutyReadinessReasonCode.ChapterInjuryThreshold
                ? "Withheld by doctrine"
                : "Physically unavailable";
    }

    private static string GetCompanyIconKey(Unit company) => company.UnitTemplate.Name switch
    {
        "Veteran Company" => "elite",
        "Battle Company" => "default",
        "Tactical Company" => "default",
        "Assault Company" => "fast",
        "Devastator Company" => "heavy",
        "Scout Company" => "scout",
        _ => "chapter"
    };

    private static string GetSquadIconKey(Squad squad)
    {
        SquadTypes type = squad.SquadTemplate.SquadType;
        if ((type & SquadTypes.HQ) > 0) return "chapter";
        if ((type & SquadTypes.Elite) > 0) return "elite";
        if ((type & SquadTypes.Fast) > 0) return "fast";
        if ((type & SquadTypes.Heavy) > 0) return "heavy";
        if ((type & SquadTypes.Scout) > 0) return "scout";
        return "default";
    }
}
