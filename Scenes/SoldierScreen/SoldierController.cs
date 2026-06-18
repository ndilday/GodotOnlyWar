using Godot;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class SoldierController : Control
{
    public SoldierView SoldierView { get; set; }
    private readonly SoldierTransferService _transferService = new();
    private PlayerSoldier _selectedSoldier;
    private List<SoldierTransferOption> _openings;
    private SoldierTransferOption _selectedTransfer;

    public override void _Ready()
    {
        if (SoldierView == null)
        {
            SoldierView = GetNode<SoldierView>("SoldierView");
        }
        SoldierView.TransferTargetSelected += OnTransferTargetSelected;
    }

    private void OnTransferTargetSelected(object sender, int index)
    {
        if(index == 0)
        {
            _selectedTransfer = null;
        }
        else
        {
            _selectedTransfer = _openings[index];
            // we want to update the soldier view as if this transfer is finalized,
            // but don't actually finalize until screen closes
            PopulateSoldierHistory(_selectedSoldier);
        }
        
    }

    public void DisplaySoldierData(PlayerSoldier soldier)
    {
        _selectedSoldier = soldier;
        PopulateSoldierData(soldier);
        PopulateSoldierHistory(soldier);
        PopulateSoldierAwards(soldier);
        PopulateSergeantReport(soldier);
        PopulateSoldierInjuryReport(soldier);
        PopulateTransferOptions(soldier);
    }

    public bool FinalizeSoldierTransfer()
    {
        if (_selectedTransfer != null)
        {
            GameDataSingleton.Instance.Sector.PlayerForce.Army.PopulateSquadMap();
            _transferService.ApplyTransfer(
                _selectedSoldier,
                _selectedTransfer,
                GameDataSingleton.Instance.Sector.PlayerForce.Army.SquadMap,
                GameDataSingleton.Instance.Date);
            _selectedTransfer = null;
            return true;
        }
        return false;
    }

    private void PopulateSoldierData(PlayerSoldier soldier)
    {
        List<Tuple<string, string>> soldierData = new List<Tuple<string, string>>();
        soldierData.Add(new Tuple<string, string>("Name", soldier.Name));
        soldierData.Add(new Tuple<string, string>("Time in Service", "TBD"));
        if (soldier.AssignedSquad.BoardedLocation != null)
        {
            soldierData.Add(new Tuple<string, string>("Location", $"Aboard {soldier.AssignedSquad.BoardedLocation.Name}"));
        }
        else
        {
            soldierData.Add(new Tuple<string, string>("Location", $"Region {soldier.AssignedSquad.CurrentRegion.Id}, {soldier.AssignedSquad.CurrentRegion.Planet.Name}, Subsector TBD"));
        }
        SoldierView.PopulateSoldierData(soldierData);
    }

    private void PopulateSoldierHistory(PlayerSoldier soldier)
    {
        SoldierView.PopulateSoldierHistory(_transferService.PreviewHistory(soldier, _selectedTransfer, GameDataSingleton.Instance.Date));
    }

    private void PopulateSoldierAwards(PlayerSoldier soldier)
    {
        List<string> soldierAwards = new List<string>();
        foreach (var award in soldier.SoldierAwards)
        {
            soldierAwards.Add($"{award.DateAwarded.ToString()}: {award.Name}");
        }
        SoldierView.PopulateSoldierAwards(soldierAwards);
    }

    private void PopulateSoldierInjuryReport(PlayerSoldier soldier)
    {
        SoldierView.PopulateSoldierInjuryReport(GenerateSoldierInjurySummary(soldier));
    }

    private void PopulateSergeantReport(PlayerSoldier soldier)
    {
        string squadType = soldier.AssignedSquad.SquadTemplate.Name;
        SoldierEvaluation evaluation = soldier.SoldierEvaluationHistory[soldier.SoldierEvaluationHistory.Count - 1];
        string name = soldier.Name;
        SoldierView.PopulateSergeantReport(GetSergeantDescription(name, evaluation, squadType));
    }

    private void PopulateTransferOptions(PlayerSoldier soldier)
    {
        _openings = _transferService.GetTransferOptions(
            GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle,
            soldier,
            true);
        SoldierView.PopulateTransferOptions(_openings.Select(o => o.DisplayName).ToList());
    }

    private string GetSergeantDescription(string name, SoldierEvaluation evaluation, string squadType)
    {
        //determine highest level soldier is rated for
        // sgt level requires silver gun and sword, plus silver leadership
        // tactical requires silver level gun and sword skills
        // assault requires silver level sword skills and bronze gun skills
        // devestator requires bronze gun skills

        // maxLevel -> Scout:0; Devestator:1; Assault:2; Tactical:3; Sergeant:4
        int maxLevel = 0;
        if(evaluation.RangedRating > 105 && evaluation.MeleeRating < 90)
        {
            maxLevel = 1;
        }
        else if(evaluation.RangedRating > 105 && evaluation.MeleeRating > 90)
        {
            if (evaluation.RangedRating > 110 && evaluation.MeleeRating > 95)
            {
                if (evaluation.LeadershipRating > 55)
                {
                    maxLevel = 4;
                }
                else
                {
                    maxLevel = 3;
                }
            }
            else
            {
                maxLevel = 2;
            }
        }
        if("Scout Squad" == squadType || "Scout HQ Squad" == squadType)
        {
            if (maxLevel > 0)
            {
                return name + " is ready for his Black Carapace and assignment to a Devastator Squad.";
            }
            else
            {
                return name + " is not ready to become a Battle Brother, and should acquire more seasoning before taking the Black Carapace.";
            }
        }
        if("Devastator Squad" == squadType)
        {
            if (maxLevel > 1)
            {
                return name + " has shown sufficient capabilities to be ready for a spot on an assault squad.";
            }
            else
            {
                return name + " still has much to learn before being ready for promotion to an assault squad.";
            }
        }
        if ("Assault Squad" == squadType)
        {
            if (maxLevel > 2)
            {
                return name + " has sufficient skill with both gun and blade to be ready for a posting to a tactical squad.";
            }
            else
            {
                return name + " is not yet fully comfortable with all forms of combat, and should remain in an assault squad for more seasoning.";
            }
        }
        if ("Tactical Squad" == squadType)
        {
            if (maxLevel > 3)
            {
                return name + " has shown leadership potential, and should be a candidate for sergeant.";
            }
            else
            {
                return name + " is performing well in his current role.";
            }
        }
        else
        {
            return "I have no opinion on future assignments for " + name + ".";
        }
    }

    private string GenerateSoldierInjurySummary(ISoldier selectedSoldier)
    {
        string summary = selectedSoldier.Name + "\n";
        byte recoveryTime = 0;
        bool isSevered = false;
        foreach (HitLocation hl in selectedSoldier.Body.HitLocations)
        {
            if (hl.Wounds.WoundTotal != 0)
            {
                if (hl.IsSevered)
                {
                    isSevered = true;
                }
                byte woundTime = hl.Wounds.RecoveryTimeLeft();
                if (woundTime > recoveryTime)
                {
                    recoveryTime = woundTime;
                }
                summary += hl.ToString() + "\n";
            }
        }
        if (isSevered)
        {
            summary += selectedSoldier.Name +
                " will be unable to perform field duties until receiving cybernetic replacements\n";
        }
        else if (recoveryTime > 0)
        {
            summary += selectedSoldier.Name +
                " requires " + recoveryTime.ToString() + " weeks to be fully fit for duty\n";
        }
        else
        {
            summary += selectedSoldier.Name +
                " is fully fit and ready to serve the Emperor\n";
        }
        return summary;
    }
}
