using Godot;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class ChapterController : Control
{
    private readonly ChapterBrowserNavigator _navigator = new();

    public ChapterView ChapterView { get; set; }

    public event EventHandler<int> SoldierSelectedForDisplay;
    public event EventHandler CloseButtonPressed;

    public override void _Ready()
    {
        if (ChapterView == null)
        {
            ChapterView = GetNode<ChapterView>("ChapterView");
        }

        ChapterView.CloseButtonPressed += OnCloseButtonPressed;
        ChapterView.BrowserItemSelected += OnBrowserItemSelected;
        ChapterView.BrowserItemDrillRequested += OnBrowserItemDrillRequested;
        ChapterView.BreadcrumbPressed += OnBreadcrumbPressed;
        ChapterView.DetailPrimaryActionPressed += OnDetailPrimaryActionPressed;

        RenderCurrentPath();
    }

    public override void _ExitTree()
    {
        if (ChapterView == null)
        {
            return;
        }

        ChapterView.CloseButtonPressed -= OnCloseButtonPressed;
        ChapterView.BrowserItemSelected -= OnBrowserItemSelected;
        ChapterView.BrowserItemDrillRequested -= OnBrowserItemDrillRequested;
        ChapterView.BreadcrumbPressed -= OnBreadcrumbPressed;
        ChapterView.DetailPrimaryActionPressed -= OnDetailPrimaryActionPressed;
    }

    public void PopulateCompanyList()
    {
        _navigator.ResetToChapter();
        RenderCurrentPath();
    }

    private void OnCloseButtonPressed(object sender, EventArgs e)
    {
        CloseButtonPressed?.Invoke(this, e);
    }

    private void OnBrowserItemSelected(object sender, ChapterBrowserItemEvent item)
    {
        if (item.Level == ChapterBrowserLevel.Soldier &&
            _navigator.Path.Level == ChapterBrowserLevel.Soldier)
        {
            _navigator.DrillInto(item);
        }
        else
        {
            _navigator.Select(item);
        }
        RenderCurrentPath();
    }

    private void OnBrowserItemDrillRequested(object sender, ChapterBrowserItemEvent item)
    {
        _navigator.DrillInto(item);
        RenderCurrentPath();
    }

    private void OnBreadcrumbPressed(object sender, ChapterBrowserLevel level)
    {
        _navigator.MoveToBreadcrumb(level);
        RenderCurrentPath();
    }

    private void OnDetailPrimaryActionPressed(object sender, EventArgs e)
    {
        ISoldier soldier = TryGetCurrentDetailSoldier();
        if (soldier != null)
        {
            SoldierSelectedForDisplay?.Invoke(this, soldier.Id);
        }
    }

    private void RenderCurrentPath()
    {
        Unit chapter = TryGetChapter();
        if (chapter == null)
        {
            RenderNoChapterData();
            return;
        }

        ChapterView.SetBreadcrumbs(BuildBreadcrumbs(chapter));

        switch (_navigator.Path.Level)
        {
            case ChapterBrowserLevel.Chapter:
                RenderChapterLevel(chapter);
                break;
            case ChapterBrowserLevel.Company:
                RenderCompanyLevel(GetCompany(_navigator.Path.CompanyId.Value));
                break;
            case ChapterBrowserLevel.Squad:
                RenderSquadLevel(GetSquad(_navigator.Path.SquadId.Value));
                break;
            case ChapterBrowserLevel.Soldier:
                RenderSoldierLevel(GetSoldier(_navigator.Path.SoldierId.Value));
                break;
        }
    }

    private void RenderNoChapterData()
    {
        ChapterView.SetBreadcrumbs(
        [
            new ChapterBreadcrumbItem(ChapterBrowserLevel.Chapter, "Chapter", "chapter")
        ]);
        ChapterView.SetLeftMenu("Companies", "No chapter loaded", []);
        ChapterView.SetDetail(new ChapterBrowserDetail(
            "chapter",
            "No Chapter Data",
            "Chapter data will appear here once a game is loaded.",
            [
                new ChapterBrowserMetric("0", "Soldiers"),
                new ChapterBrowserMetric("0", "Squads"),
                new ChapterBrowserMetric("0", "Wounded")
            ],
            [
                new ChapterBrowserDetailCard("archive", "Awaiting Game Data", "No active chapter", "Open this screen through the main game flow to browse companies, squads, and soldiers.")
            ]));
    }

    private void RenderChapterLevel(Unit chapter)
    {
        List<ChapterBrowserMenuItem> chapterItems = chapter.Squads
            .Select(squad => new ChapterBrowserMenuItem(
                ChapterBrowserLevel.Squad,
                squad.Id,
                GetSquadIconKey(squad),
                squad.Name,
                $"{squad.SquadTemplate.Name} - {squad.Members.Count} soldiers",
                true,
                IsSelected(ChapterBrowserLevel.Squad, squad.Id),
                ">"))
            .ToList();

        chapterItems.AddRange(chapter.ChildUnits
            .Select(company => new ChapterBrowserMenuItem(
                ChapterBrowserLevel.Company,
                company.Id,
                GetCompanyIconKey(company),
                company.Name,
                $"{company.Squads.Count} squads - {company.GetAllMembers().Count()} soldiers",
                true,
                IsSelected(ChapterBrowserLevel.Company, company.Id),
                ">"))
            .ToList());

        ChapterView.SetLeftMenu("Chapter Command", "Select / drill", chapterItems);

        Unit selectedCompany = TryGetSelectedCompany();
        Squad selectedSquad = TryGetSelectedSquad();
        if (selectedCompany == null && selectedSquad == null)
        {
            selectedSquad = chapter.Squads.FirstOrDefault();
            selectedCompany = selectedSquad == null ? chapter.ChildUnits.FirstOrDefault() : null;
        }
        ChapterView.SetDetail(BuildChapterDetail(chapter, selectedCompany, selectedSquad));
    }

    private void RenderCompanyLevel(Unit company)
    {
        List<ChapterBrowserMenuItem> squads = company.Squads
            .Select(squad => new ChapterBrowserMenuItem(
                ChapterBrowserLevel.Squad,
                squad.Id,
                GetSquadIconKey(squad),
                squad.Name,
                $"{squad.SquadTemplate.Name} - {squad.Members.Count} soldiers",
                true,
                IsSelected(ChapterBrowserLevel.Squad, squad.Id),
                ">"))
            .ToList();

        ChapterView.SetLeftMenu($"{company.Name} Squads", "Select / drill", squads);

        Squad selectedSquad = TryGetSelectedSquad() ?? company.Squads.FirstOrDefault();
        ChapterView.SetDetail(BuildCompanyDetail(company, selectedSquad));
    }

    private void RenderSquadLevel(Squad squad)
    {
        List<ChapterBrowserMenuItem> soldiers = squad.Members
            .Select(soldier => new ChapterBrowserMenuItem(
                ChapterBrowserLevel.Soldier,
                soldier.Id,
                GetSoldierIconKey(soldier),
                $"{soldier.Template.Name} {soldier.Name}",
                soldier.CanFight ? "Available" : "Wounded or impaired",
                true,
                IsSelected(ChapterBrowserLevel.Soldier, soldier.Id),
                "i"))
            .ToList();

        ChapterView.SetLeftMenu("Battle Brothers", "Select / profile", soldiers);

        ISoldier selectedSoldier = TryGetSelectedSoldier() ?? squad.Members.FirstOrDefault();
        ChapterView.SetDetail(selectedSoldier == null ? BuildSquadDetail(squad, null) : BuildSoldierDetail(selectedSoldier));
    }

    private void RenderSoldierLevel(ISoldier soldier)
    {
        Squad squad = soldier.AssignedSquad;
        List<ChapterBrowserMenuItem> soldiers = squad.Members
            .Select(squadMember => new ChapterBrowserMenuItem(
                ChapterBrowserLevel.Soldier,
                squadMember.Id,
                GetSoldierIconKey(squadMember),
                $"{squadMember.Template.Name} {squadMember.Name}",
                squadMember.CanFight ? "Available" : "Wounded or impaired",
                true,
                squadMember.Id == soldier.Id,
                "i"))
            .ToList();

        ChapterView.SetLeftMenu("Battle Brothers", "Selected soldier", soldiers);
        ChapterView.SetDetail(BuildSoldierDetail(soldier));
    }

    private IReadOnlyList<ChapterBreadcrumbItem> BuildBreadcrumbs(Unit chapter)
    {
        List<ChapterBreadcrumbItem> breadcrumbs =
        [
            new ChapterBreadcrumbItem(ChapterBrowserLevel.Chapter, "Chapter", "chapter")
        ];

        if (_navigator.Path.CompanyId.HasValue)
        {
            Unit company = GetCompany(_navigator.Path.CompanyId.Value);
            breadcrumbs.Add(new ChapterBreadcrumbItem(ChapterBrowserLevel.Company, company.Name, GetCompanyIconKey(company)));
        }

        if (_navigator.Path.SquadId.HasValue)
        {
            Squad squad = GetSquad(_navigator.Path.SquadId.Value);
            breadcrumbs.Add(new ChapterBreadcrumbItem(ChapterBrowserLevel.Squad, squad.Name, GetSquadIconKey(squad)));
        }

        if (_navigator.Path.SoldierId.HasValue)
        {
            ISoldier soldier = GetSoldier(_navigator.Path.SoldierId.Value);
            breadcrumbs.Add(new ChapterBreadcrumbItem(ChapterBrowserLevel.Soldier, soldier.Name, GetSoldierIconKey(soldier)));
        }

        return breadcrumbs;
    }

    private ChapterBrowserDetail BuildChapterDetail(Unit chapter, Unit selectedCompany, Squad selectedSquad)
    {
        int soldierCount = chapter.GetAllMembers().Count();
        int squadCount = chapter.GetAllSquads().Count();
        int woundedCount = chapter.GetAllMembers().Count(soldier => !soldier.CanFight);

        List<ChapterBrowserDetailCard> cards =
        [
            new ChapterBrowserDetailCard("chapter", "Chapter Strength", chapter.Name, $"{soldierCount} battle brothers across {squadCount} squads."),
            new ChapterBrowserDetailCard("medical", "Recovery", "Apothecarium demand", $"{woundedCount} soldiers are wounded or impaired."),
            new ChapterBrowserDetailCard("training", "Training Pipeline", "Chapter development", "Drill into the Tenth Company to review scout and initiate progress.")
        ];

        if (selectedCompany != null)
        {
            cards.Insert(0, new ChapterBrowserDetailCard(
                GetCompanyIconKey(selectedCompany),
                $"Selected: {selectedCompany.Name}",
                selectedCompany.UnitTemplate.Name,
                $"{selectedCompany.Squads.Count} squads, {selectedCompany.GetAllMembers().Count()} soldiers."));
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

    private ChapterBrowserDetail BuildCompanyDetail(Unit company, Squad selectedSquad)
    {
        int soldierCount = company.GetAllMembers().Count();
        int woundedCount = company.GetAllMembers().Count(soldier => !soldier.CanFight);

        List<ChapterBrowserDetailCard> cards =
        [
            new ChapterBrowserDetailCard(GetCompanyIconKey(company), "Company Strength", company.UnitTemplate.Name, $"{soldierCount} soldiers across {company.Squads.Count} squads."),
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
                new ChapterBrowserMetric(company.Squads.Count.ToString(), "Squads"),
                new ChapterBrowserMetric(woundedCount.ToString(), "Wounded")
            ],
            cards);
    }

    private ChapterBrowserDetail BuildSquadDetail(Squad squad, ISoldier selectedSoldier)
    {
        int woundedCount = squad.Members.Count(soldier => !soldier.CanFight);

        List<ChapterBrowserDetailCard> cards =
        [
            new ChapterBrowserDetailCard(GetSquadIconKey(squad), "Squad Composition", squad.SquadTemplate.Name, $"{squad.Members.Count} battle brothers assigned."),
            new ChapterBrowserDetailCard("medical", "Casualties", "Current condition", $"{woundedCount} soldiers are wounded or impaired."),
            new ChapterBrowserDetailCard("archive", "Squad Record", "Chronicle", "Squad history, honors, and mission record can expand here.")
        ];

        if (selectedSoldier != null)
        {
            cards.Insert(0, new ChapterBrowserDetailCard(
                GetSoldierIconKey(selectedSoldier),
                $"Selected: {selectedSoldier.Template.Name} {selectedSoldier.Name}",
                selectedSoldier.CanFight ? "Available" : "Wounded or impaired",
                "Select a soldier for preview; use the detail button to open the existing soldier display flow."));
        }

        return new ChapterBrowserDetail(
            GetSquadIconKey(squad),
            squad.Name,
            "Squad-level overview. Select individual soldiers to inspect their status.",
            [
                new ChapterBrowserMetric(squad.Members.Count.ToString(), "Soldiers"),
                new ChapterBrowserMetric(woundedCount.ToString(), "Wounded"),
                new ChapterBrowserMetric(squad.SquadTemplate.BattleValue.ToString(), "Battle Value")
            ],
            cards);
    }

    private ChapterBrowserDetail BuildSoldierDetail(ISoldier soldier)
    {
        return new ChapterBrowserDetail(
            GetSoldierIconKey(soldier),
            $"{soldier.Template.Name} {soldier.Name}",
            soldier.CanFight ? "Available for duty." : "Wounded or impaired.",
            [
                new ChapterBrowserMetric(Mathf.RoundToInt(soldier.Strength).ToString(), "Strength"),
                new ChapterBrowserMetric(Mathf.RoundToInt(soldier.Dexterity).ToString(), "Dexterity"),
                new ChapterBrowserMetric(Mathf.RoundToInt(soldier.Charisma).ToString(), "Presence")
            ],
            [
                new ChapterBrowserDetailCard(GetSoldierIconKey(soldier), "Profile", soldier.Template.Name, $"Assigned to {soldier.AssignedSquad?.Name ?? "no squad"}."),
                new ChapterBrowserDetailCard("medical", "Condition", soldier.CanFight ? "Ready" : "Wounded", $"Functioning hands: {soldier.FunctioningHands}."),
                new ChapterBrowserDetailCard("archive", "Record", "Chronicle", "Detailed battle history can be connected in a later implementation slice.")
            ],
            "Open Full Record",
            "archive");
    }

    private bool IsSelected(ChapterBrowserLevel level, int id)
    {
        if (_navigator.Path.Level == level)
        {
            return level switch
            {
                ChapterBrowserLevel.Company => _navigator.Path.CompanyId == id,
                ChapterBrowserLevel.Squad => _navigator.Path.SquadId == id,
                ChapterBrowserLevel.Soldier => _navigator.Path.SoldierId == id,
                _ => false
            };
        }

        return _navigator.SelectedItem != null &&
            _navigator.SelectedItem.Level == level &&
            _navigator.SelectedItem.Id == id;
    }

    private Unit TryGetSelectedCompany()
    {
        if (_navigator.SelectedItem == null || _navigator.SelectedItem.Level != ChapterBrowserLevel.Company)
        {
            return null;
        }
        return GetChapter().ChildUnits.FirstOrDefault(company => company.Id == _navigator.SelectedItem.Id);
    }

    private Squad TryGetSelectedSquad()
    {
        if (_navigator.SelectedItem == null || _navigator.SelectedItem.Level != ChapterBrowserLevel.Squad)
        {
            return null;
        }
        return GetChapter().GetAllSquads().FirstOrDefault(squad => squad.Id == _navigator.SelectedItem.Id);
    }

    private ISoldier TryGetSelectedSoldier()
    {
        if (_navigator.SelectedItem == null || _navigator.SelectedItem.Level != ChapterBrowserLevel.Soldier)
        {
            return null;
        }
        return GetChapter().GetAllMembers().FirstOrDefault(soldier => soldier.Id == _navigator.SelectedItem.Id);
    }

    private ISoldier TryGetCurrentDetailSoldier()
    {
        if (_navigator.Path.SoldierId.HasValue)
        {
            return GetSoldier(_navigator.Path.SoldierId.Value);
        }

        return TryGetSelectedSoldier();
    }

    private Unit GetChapter()
    {
        return TryGetChapter();
    }

    private Unit TryGetChapter()
    {
        return GameDataSingleton.Instance?.Sector?.PlayerForce?.Army?.OrderOfBattle;
    }

    private Unit GetCompany(int companyId)
    {
        return GetChapter().ChildUnits.First(company => company.Id == companyId);
    }

    private Squad GetSquad(int squadId)
    {
        return GetChapter().GetAllSquads().First(squad => squad.Id == squadId);
    }

    private ISoldier GetSoldier(int soldierId)
    {
        return GetChapter().GetAllMembers().First(soldier => soldier.Id == soldierId);
    }

    private static string GetCompanyIconKey(Unit company)
    {
        return company.UnitTemplate.Name switch
        {
            "Veteran Company" => "elite",
            "Battle Company" => "default",
            "Tactical Company" => "default",
            "Assault Company" => "fast",
            "Devastator Company" => "heavy",
            "Scout Company" => "scout",
            _ => "chapter"
        };
    }

    private static string GetSquadIconKey(Squad squad)
    {
        SquadTypes type = squad.SquadTemplate.SquadType;
        if ((type & SquadTypes.HQ) > 0)
        {
            return "chapter";
        }
        if ((type & SquadTypes.Elite) > 0)
        {
            return "elite";
        }
        if ((type & SquadTypes.Fast) > 0)
        {
            return "fast";
        }
        if ((type & SquadTypes.Heavy) > 0)
        {
            return "heavy";
        }
        if ((type & SquadTypes.Scout) > 0)
        {
            return "scout";
        }

        return "default";
    }

    private static string GetSoldierIconKey(ISoldier soldier)
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
}
