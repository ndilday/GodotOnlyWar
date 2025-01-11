using Godot;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class SoldierController : Control
{
	public SoldierView SoldierView { get; set; }
	private PlayerSoldier _selectedSoldier;
	private List<Tuple<int, SoldierTemplate, string>> _openings;
	private Tuple<int, SoldierTemplate, string> _selectedTransfer;

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
			PopulateSoldierHistory(_selectedSoldier, _selectedTransfer);
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
			Squad currentSquad = _selectedSoldier.AssignedSquad;
			// move soldier to his new role
			currentSquad.RemoveSquadMember(_selectedSoldier);
			if (_selectedSoldier.Template.IsSquadLeader
				&& (currentSquad.SquadTemplate.SquadType & SquadTypes.HQ) == 0)
			{
				// if soldier is squad leader and its not an HQ Squad, change name
				currentSquad.Name = currentSquad.SquadTemplate.Name;
			}
			Squad newSquad = GameDataSingleton.Instance.Sector.PlayerForce.Army.SquadMap[_selectedTransfer.Item1];
			newSquad.AddSquadMember(_selectedSoldier);

			UpdateSquadLocations(currentSquad, newSquad);

			Date date = GameDataSingleton.Instance.Date;
			if (_selectedSoldier.Template != _selectedTransfer.Item2)
			{
				_selectedSoldier.AddEntryToHistory($"{date}: promoted to {_selectedTransfer.Item2.Name}");
				_selectedSoldier.Template = _selectedTransfer.Item2;
			}
			if (_selectedSoldier.Template.IsSquadLeader
				&& (newSquad.SquadTemplate.SquadType & SquadTypes.HQ) == 0)
			{
				// if soldier is squad leader and its not an HQ Squad, change name
				newSquad.Name = _selectedSoldier.Name.Split(' ')[1] + " Squad";
			}
			
			if (currentSquad.Members.Count == 0 &&
			   (currentSquad.SquadTemplate.SquadType & SquadTypes.Scout) == SquadTypes.Scout)
			{
				// delete scout squads when they're emptied out
				Unit parentUnit = currentSquad.ParentUnit;
				parentUnit.RemoveSquad(currentSquad);
				GameDataSingleton.Instance.Sector.PlayerForce.Army.SquadMap.Remove(currentSquad.Id);
			}
			if (currentSquad != newSquad)
			{
				_selectedSoldier.AddEntryToHistory($"{date}: transferred to {_selectedTransfer.Item3}");
			}
			_selectedTransfer = null;
			return true;
		}
		return false;
	}

	private void UpdateSquadLocations(Squad oldSquad, Squad newSquad)
	{
		if (newSquad.Members.Count == 1)
		{
			// make the location of the new squad the same as the old one
			newSquad.CurrentRegion = oldSquad.CurrentRegion;
			newSquad.BoardedLocation = oldSquad.BoardedLocation;
		}
		if (oldSquad.Members.Count == 0)
		{
			oldSquad.CurrentRegion = null;
			oldSquad.BoardedLocation = null;
		}
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

	private void PopulateSoldierHistory(PlayerSoldier soldier, Tuple<int, SoldierTemplate, string> newRole = null)
	{
		List<string> soldierHistory = new List<string>();
		foreach (var entry in soldier.SoldierHistory)
		{
			soldierHistory.Add(entry);
		}
		if(newRole != null)
		{
			Date date = GameDataSingleton.Instance.Date;
			if (_selectedSoldier.Template != newRole.Item2)
			{
				soldierHistory.Add($"{date}: promoted to {newRole.Item2.Name}");
			}
			if (soldier.AssignedSquad.Id != newRole.Item1)
			{
				_selectedSoldier.AddEntryToHistory($"{date}: transferred to {_selectedTransfer.Item3}");
			}
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
		_openings = GetOpeningsInUnit(GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle, soldier.AssignedSquad, soldier.Template);
		// insert current assignment at top
		_openings.Insert(0, new Tuple<int, SoldierTemplate, string>(soldier.AssignedSquad.Id, soldier.Template,
			$"{soldier.Template.Name}, {soldier.AssignedSquad.Name}, {soldier.AssignedSquad.ParentUnit.Name}"));
		SoldierView.PopulateTransferOptions(_openings.Select(o => o.Item3).ToList());
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

	private List<Tuple<int, SoldierTemplate, string>> GetOpeningsInUnit(Unit unit, Squad currentSquad,
																			SoldierTemplate soldierTemplate)
	{
		List<Tuple<int, SoldierTemplate, string>> openSlots =
			new List<Tuple<int, SoldierTemplate, string>>();
		IEnumerable<SoldierTemplate> squadSlots;
		foreach (Squad squad in unit.Squads)
		{
			squadSlots = GetOpeningsInSquad(squad, currentSquad, soldierTemplate);
			if (squadSlots.Count() > 0)
			{
				foreach (SoldierTemplate template in squadSlots)
				{
					openSlots.Add(new Tuple<int, SoldierTemplate, string>(squad.Id, template,
						$"{template.Name}, {squad.Name}, {unit.Name}"));
				}
			}
		}
		foreach (Unit childUnit in unit.ChildUnits ?? Enumerable.Empty<Unit>())
		{
			openSlots.AddRange(GetOpeningsInUnit(childUnit, currentSquad, soldierTemplate));
		}
		return openSlots;
	}

	private IEnumerable<SoldierTemplate> GetOpeningsInSquad(Squad squad, Squad currentSquad,
															SoldierTemplate soldierTemplate)
	{
		List<SoldierTemplate> openSpots = new List<SoldierTemplate>();
		bool hasSquadLeader = squad.SquadLeader != null;
		// get the count of each soldier type in the squad
		// compare to the max count of each type
		Dictionary<SoldierTemplate, int> typeCountMap =
			squad.Members.GroupBy(s => s.Template)
						 .ToDictionary(g => g.Key, g => g.Count());
		foreach (SquadTemplateElement element in squad.SquadTemplate.Elements)
		{
			// if the squad has no squad leader, only squad leader elements can be added now
			if (!hasSquadLeader && !element.SoldierTemplate.IsSquadLeader)
			{
				continue;
			}
			if (currentSquad == squad && element.SoldierTemplate == soldierTemplate)
			{
				continue;
			}
			if (element.SoldierTemplate.Rank < soldierTemplate.Rank
				|| element.SoldierTemplate.Rank > soldierTemplate.Rank + 1)
			{
				continue;
			}
			int existingHeadcount = 0;
			if (typeCountMap.ContainsKey(element.SoldierTemplate))
			{
				existingHeadcount += typeCountMap[element.SoldierTemplate];
			}
			if (existingHeadcount < element.MaximumNumber)
			{
				openSpots.Add(element.SoldierTemplate);
			}
		}
		return openSpots;
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
