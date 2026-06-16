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
    private readonly ChapterBrowserPath _path = new();
    private ChapterBrowserItemEvent _selectedItem;

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

        _selectedItem = null;
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
    }

    public void PopulateCompanyList()
    {
        _path.TrimTo(ChapterBrowserLevel.Chapter);
        _selectedItem = null;
        RenderCurrentPath();
    }

    private void OnCloseButtonPressed(object sender, EventArgs e)
    {
        CloseButtonPressed?.Invoke(this, e);
    }

    private void OnBrowserItemSelected(object sender, ChapterBrowserItemEvent item)
    {
        _selectedItem = item;
        RenderCurrentPath();

        if (item.Level == ChapterBrowserLevel.Soldier)
        {
            SoldierSelectedForDisplay?.Invoke(this, item.Id);
        }
    }

    private void OnBrowserItemDrillRequested(object sender, ChapterBrowserItemEvent item)
    {
        switch (item.Level)
        {
            case ChapterBrowserLevel.Company:
                _path.CompanyId = item.Id;
                _path.SquadId = null;
                _path.SoldierId = null;
                _selectedItem = null;
                break;
            case ChapterBrowserLevel.Squad:
                _path.SquadId = item.Id;
                _path.SoldierId = null;
                _selectedItem = null;
                break;
            case ChapterBrowserLevel.Soldier:
                _path.SoldierId = item.Id;
                _selectedItem = item;
                SoldierSelectedForDisplay?.Invoke(this, item.Id);
                break;
        }

        RenderCurrentPath();
    }

    private void OnBreadcrumbPressed(object sender, ChapterBrowserLevel level)
    {
        _path.TrimTo(level);
        _selectedItem = null;
        RenderCurrentPath();
    }

    private void RenderCurrentPath()
    {
        Unit chapter = GetChapter();
        ChapterView.SetBreadcrumbs(BuildBreadcrumbs(chapter));

        switch (_path.Level)
        {
            case ChapterBrowserLevel.Chapter:
                RenderChapterLevel(chapter);
                break;
            case ChapterBrowserLevel.Company:
                RenderCompanyLevel(GetCompany(_path.CompanyId.Value));
                break;
            case ChapterBrowserLevel.Squad:
                RenderSquadLevel(GetSquad(_path.SquadId.Value));
                break;
            case ChapterBrowserLevel.Soldier:
                RenderSoldierLevel(GetSoldier(_path.SoldierId.Value));
                break;
        }
    }

    private void RenderChapterLevel(Unit chapter)
    {
        List<ChapterBrowserMenuItem> companies = chapter.ChildUnits
            .Select(company => new ChapterBrowserMenuItem(
                ChapterBrowserLevel.Company,
                company.Id,
                GetCompanyIconKey(company),
                company.Name,
                $"{company.Squads.Count} squads - {company.GetAllMembers().Count()} soldiers",
                true,
                IsSelected(ChapterBrowserLevel.Company, company.Id)))
            .ToList();

        ChapterView.SetLeftMenu("Companies", "Select previews; drill opens squads", companies);

        Unit selectedCompany = TryGetSelectedCompany() ?? chapter.ChildUnits.FirstOrDefault();
        ChapterView.SetDetail(BuildChapterDetail(chapter, selectedCompany));
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
                IsSelected(ChapterBrowserLevel.Squad, squad.Id)))
            .ToList();

        ChapterView.SetLeftMenu($"{company.Name} Squads", "Select previews; drill opens soldiers", squads);

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
                false,
                IsSelected(ChapterBrowserLevel.Soldier, soldier.Id)))
            .ToList();

        ChapterView.SetLeftMenu("Battle Brothers", "Select a soldier for details", soldiers);

        ISoldier selectedSoldier = TryGetSelectedSoldier() ?? squad.Members.FirstOrDefault();
        ChapterView.SetDetail(BuildSquadDetail(squad, selectedSoldier));
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
                false,
                squadMember.Id == soldier.Id))
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

        if (_path.CompanyId.HasValue)
        {
            Unit company = GetCompany(_path.CompanyId.Value);
            breadcrumbs.Add(new ChapterBreadcrumbItem(ChapterBrowserLevel.Company, company.Name, GetCompanyIconKey(company)));
        }

        if (_path.SquadId.HasValue)
        {
            Squad squad = GetSquad(_path.SquadId.Value);
            breadcrumbs.Add(new ChapterBreadcrumbItem(ChapterBrowserLevel.Squad, squad.Name, GetSquadIconKey(squad)));
        }

        if (_path.SoldierId.HasValue)
        {
            ISoldier soldier = GetSoldier(_path.SoldierId.Value);
            breadcrumbs.Add(new ChapterBreadcrumbItem(ChapterBrowserLevel.Soldier, soldier.Name, GetSoldierIconKey(soldier)));
        }

        return breadcrumbs;
    }

    private ChapterBrowserDetail BuildChapterDetail(Unit chapter, Unit selectedCompany)
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

        return new ChapterBrowserDetail(
            "chapter",
            chapter.Name,
            "Chapter-level overview. Select a company for a preview; drill into it to manage squads.",
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
            ]);
    }

    private bool IsSelected(ChapterBrowserLevel level, int id)
    {
        return _selectedItem != null && _selectedItem.Level == level && _selectedItem.Id == id;
    }

    private Unit TryGetSelectedCompany()
    {
        if (_selectedItem == null || _selectedItem.Level != ChapterBrowserLevel.Company)
        {
            return null;
        }
        return GetChapter().ChildUnits.FirstOrDefault(company => company.Id == _selectedItem.Id);
    }

    private Squad TryGetSelectedSquad()
    {
        if (_selectedItem == null || _selectedItem.Level != ChapterBrowserLevel.Squad)
        {
            return null;
        }
        return GetChapter().GetAllSquads().FirstOrDefault(squad => squad.Id == _selectedItem.Id);
    }

    private ISoldier TryGetSelectedSoldier()
    {
        if (_selectedItem == null || _selectedItem.Level != ChapterBrowserLevel.Soldier)
        {
            return null;
        }
        return GetChapter().GetAllMembers().FirstOrDefault(soldier => soldier.Id == _selectedItem.Id);
    }

    private Unit GetChapter()
    {
        return GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle;
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
            "Battle Company" => "tactical",
            "Tactical Company" => "tactical",
            "Assault Company" => "assault",
            "Devastator Company" => "devastator",
            "Scout Company" => "scout",
            _ => "hq"
        };
    }

    private static string GetSquadIconKey(Squad squad)
    {
        SquadTypes type = squad.SquadTemplate.SquadType;
        if ((type & SquadTypes.HQ) > 0)
        {
            return "hq";
        }
        if ((type & SquadTypes.Bodyguard) > 0)
        {
            return "bodyguard";
        }
        if ((type & SquadTypes.Scout) > 0)
        {
            return "scout";
        }
        if ((type & SquadTypes.Elite) > 0)
        {
            return "elite";
        }
        if ((type & SquadTypes.Fast) > 0)
        {
            return "assault";
        }
        if ((type & SquadTypes.Heavy) > 0)
        {
            return "devastator";
        }

        return "tactical";
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
