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
		PopulateSoldierAwards(soldier);
		PopulateSergeantReport(soldier);
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

	private void PopulateSoldierAwards(PlayerSoldier soldier)
	{
		List<string> soldierAwards = new List<string>();
		foreach (var award in soldier.SoldierAwards)
		{
			soldierAwards.Add($"{award.DateAwarded.ToString()}: {award.Name}");
		}
		SoldierView.PopulateSoldierAwards(soldierAwards);
	}

	private void PopulateSergeantReport(PlayerSoldier soldier)
	{
		string squadType = soldier.AssignedSquad.SquadTemplate.Name;
		SoldierEvaluation evaluation = soldier.SoldierEvaluationHistory[soldier.SoldierEvaluationHistory.Count - 1];
		string name = soldier.Name;
		SoldierView.PopulateSergeantReport(GetSergeantDescription(name, evaluation, squadType));
	}

	private string GetSergeantDescription(string name, SoldierEvaluation evaluation, string squadType)
	{
		//determine highest level soldier is rated for
		// sgt level requires gold gun and sword, plus some leadership
		// tactical requires silver level gun and sword skills
		// assault requires gold sword
		// 

		if (evaluation.RangedRating > 105)
		{
			if (evaluation.MeleeRating > 90)
			{
				if (evaluation.MeleeRating > 100 && evaluation.RangedRating > 105)
				{
					return name + " is ready to accept the Black Carapace and join a Devastator Squad; I think he will rise through the ranks quickly.\n";
				}
				else
				{
					return name + " is ready to be promoted to a Devastator Squad, but I would prefer he earn more seasoning first.\n";
				}
			}
			else
			{
				return name + " could be promoted in an emergency, but is not ready to face hand-to-hand combat.\n";
			}
		}
		else if (evaluation.MeleeRating > 90)
		{
			return name + " has a good grasp of the sword, but his mastery of the bolter leaves something to be desired.\n";
		}
		else
		{
			return name + " is not ready to become a Battle Brother, and should acquire more seasoning before taking the Black Carapace.\n";
		}
	}
}
