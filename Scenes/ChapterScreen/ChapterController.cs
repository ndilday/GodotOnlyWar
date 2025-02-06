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
    public ChapterView ChapterView {get;set;}

    public event EventHandler<int> SoldierSelectedForDisplay;
    public event EventHandler CloseButtonPressed;

    public override void _Ready()
    {
        if (ChapterView == null)
        {
            ChapterView = GetNode<ChapterView>("ChapterView");
            ChapterView.CloseButtonPressed += (object sender, EventArgs e) => CloseButtonPressed?.Invoke(this, e);
        }
        ChapterView.CompanyButtonPressed += OnCompanyButtonPressed;
        ChapterView.SquadButtonPressed += OnSquadButtonPressed;
        ChapterView.SoldierButtonPressed += OnSoldierButtonPressed;
        PopulateCompanyList();
    } 

    public override void _ExitTree()
    {
        if (ChapterView != null)
        {
            ChapterView.CompanyButtonPressed -= OnCompanyButtonPressed;
            ChapterView.SquadButtonPressed -= OnSquadButtonPressed;
            ChapterView.SoldierButtonPressed -= OnSoldierButtonPressed;
        }
    }

    private void OnCompanyButtonPressed(object sender, int companyId)
    {
        // get company data
        Unit company = GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle.ChildUnits.Find(c => c.Id == companyId);
        // get squad data
        List<Tuple<int, string>> squadList = [];
        foreach (Squad squad in company.Squads)
        {
            squadList.Add(new Tuple<int, string>(squad.Id, $"{squad.Name} ({squad.SquadTemplate.Name})"));
        }
        ChapterView.PopulateSquadList(squadList);
    }

    private void OnSquadButtonPressed(object sender, int squadId)
    {
        // get squad data
        Squad squad = GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle.GetAllSquads().First(s => s.Id == squadId);
        // get soldier data
        List<Tuple<int, string>> soldierList = [];
        foreach (ISoldier soldier in squad.Members)
        {
            soldierList.Add(new Tuple<int, string>(soldier.Id, $"{soldier.Template.Name} {soldier.Name}"));
        }
        ChapterView.PopulateSoldierList(soldierList);
    }

    private void OnSoldierButtonPressed(object sender, int soldierId)
    {
        SoldierSelectedForDisplay?.Invoke(this, soldierId);
    }


    public void PopulateCompanyList()
    {
        List<Tuple<int, CompanyType, string>> companyList = [];
        foreach (Unit company in GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle.ChildUnits)
        {
            CompanyType companyType;
            switch (company.UnitTemplate.Name)
            {
                case "Veteran Company":
                    companyType = CompanyType.Veteran;
                    break;
                case "Battle Company":
                    companyType = CompanyType.Tactical;
                    break;
                case "Tactical Company":
                    companyType = CompanyType.ReserveTactical;
                    break;
                case "Assault Company":
                    companyType = CompanyType.ReserveAssault;
                    break;
                case "Devastator Company":
                    companyType = CompanyType.ReserveDevastator;
                    break;
                case "Scout Company":
                    companyType = CompanyType.Scout;
                    break;
                default:
                    GD.Print("Unknown company type: " + company.Name);
                    companyType = CompanyType.Tactical;
                    break;
            }
            companyList.Add(new Tuple<int, CompanyType, string>(company.Id, companyType, company.Name));
        }
        ChapterView.PopulateCompanyList(companyList);
    }
}
