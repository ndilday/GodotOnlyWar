using Godot;
using OnlyWar.Models.Squads;
using OnlyWar.Models;
using System;
using System.Linq;
using OnlyWar.Models.Units;
using System.Collections.Generic;
using OnlyWar.Models.Soldiers;

public partial class ConquistorumScreenController : Control
{
	ConquistorumScreenView _view;
	public event EventHandler CloseButtonPressed;

	public override void _Ready()
	{
		_view = GetNode<ConquistorumScreenView>("ConquistorumScreenView");
		_view.CloseButtonPressed += (object sender, EventArgs e) => CloseButtonPressed?.Invoke(sender, e);
		_view.SquadButtonPressed += HandleSquadButtonPressed;
	}

	private void PopulateScountSquadList()
	{
		// TODO: stop assuming a single scout company
		Unit company = GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle.ChildUnits.Find(c => c.UnitTemplate.Name == "Scout Company");
		// get squad data
		List<Tuple<int, string>> squadList = [];
		foreach (Squad squad in company.Squads)
		{
			squadList.Add(new Tuple<int, string>(squad.Id, $"{squad.Name}"));
		}
		_view.PopulateSquadList(squadList);
	}

	private void HandleSquadButtonPressed(object sender, int squadId)
	{
		Squad squad = GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle.GetAllSquads().First(s => s.Id == squadId);
		string squadReport = "";

		// should we ignore the SGT here or not?
		if (squad.Members.Count == 0)
		{
			squadReport += "This squad has no members. ";
		}
		else
		{
			foreach (PlayerSoldier soldier in squad.Members)
			{
				if (soldier.Template.IsSquadLeader)
				{
					// TODO: add code to test whether the SGT still feels he has things
					// to teach the soldiers
				}
				else
				{
					squadReport += GetSergeantDescription(soldier.Name, soldier.SoldierEvaluationHistory[soldier.SoldierEvaluationHistory.Count - 1]);
				}
			}
		}
		_view.PopulateSquadReadinessReport(squadReport);
	}

	private string GetSergeantDescription(string name, SoldierEvaluation evaluation)
	{
		//determine highest level soldier is rated for
		// sgt level requires silver gun and sword, plus silver leadership
		// tactical requires silver level gun and sword skills
		// assault requires silver level sword skills and bronze gun skills
		// devestator requires bronze gun skills

		// maxLevel -> Scout:0; Devestator:1; Assault:2; Tactical:3; Sergeant:4
		int maxLevel = 0;
		if (evaluation.RangedRating > 105 && evaluation.MeleeRating < 90)
		{
			maxLevel = 1;
		}
		else if (evaluation.RangedRating > 105 && evaluation.MeleeRating > 90)
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
		if (maxLevel == 4)
		{
			return name + " is ready for his Black Carapace and assignment to a Devastator Squad; I believe he will rise to be a Sergeant himself in short order.";
		}
		else if (maxLevel > 1)
		{
			return name + " is ready for his Black Carapace and assignment to a Devastator Squad; I believe he will rise through the ranks quickly";
		}
		else if (maxLevel == 1)
		{
			return name + " is ready for his Black Carapace and assignment to a Devastator Squad.";
		}
		else
		{
			return name + " is not ready to become a Battle Brother, and should acquire more seasoning before taking the Black Carapace.";
		}

	}
}
