using Godot;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;

public partial class SoldierController : Control
{
	public SoldierView SoldierView { get; set; }

	public override void _Ready()
	{
		if (SoldierView == null)
		{
			SoldierView = GetNode<SoldierView>("SoldierView");
		}
	}

	public void DisplaySoldierData(PlayerSoldier soldier)
	{
		PopulateSoldierData(soldier);
		PopulateSoldierHistory(soldier);
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
		List<string> soldierHistory = new List<string>();
		foreach (var entry in soldier.SoldierHistory)
		{
			soldierHistory.Add(entry);
		}
		SoldierView.PopulateSoldierHistory(soldierHistory);
	}
}
