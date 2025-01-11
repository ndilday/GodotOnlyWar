using Godot;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class ApothecariumScreenController : Control
{
	ApothecariumScreenView _view;
	private const string GENESEED_FORMAT = @"Sir! Currently, we have {0} Geneseed stored.
Within the next year, we anticipate {1} implanted Progenoid Glands will mature.";
	private const string SQUAD_FORMAT = @"{0} has {1} wounded members.
Of those, {2} are unfit for field duty under any circumstances; {3} require cybernetic replacements and will be fitted within the next few days.
It will require approximately {4} weeks before all marines in the squad (other than those replacing cybernetic replacements) are fully fit.";

	public override void _Ready()
	{
		_view = GetNode<ApothecariumScreenView>("ApothecariumScreenView");
		_view.PopulateGeneseedReport(GenerateGeneseedReport());
		_view.PopulateSquadList(GetSquadsWithInjuredSoldiers());
		_view.SquadButtonPressed += HandleSquadButtonPressed;
	}

	public override void _ExitTree()
	{
		if (_view != null)
		{
			_view.SquadButtonPressed -= HandleSquadButtonPressed;
		}
	}

	private string GenerateGeneseedReport()
	{
		ushort currentGeneseed = GameDataSingleton.Instance.Sector.PlayerForce.GeneseedStockpile;
		Date date = GameDataSingleton.Instance.Date;
		Date fourYearsAgo = new Date(date.Millenium, date.Year - 4, date.Week);
		Date fiveYearsAgo = new Date(date.Millenium, date.Year - 5, date.Week);
		Date nineYearsAgo = new Date(date.Millenium, date.Year - 9, date.Week);
		Date tenYearsAgo = new Date(date.Millenium, date.Year - 10, date.Week);
		ushort inAYear = 0;
		foreach (PlayerSoldier marine in GameDataSingleton.Instance.Sector.PlayerForce.Army.PlayerSoldierMap.Values)
		{
			Date implantDate = marine.ProgenoidImplantDate;
			if (implantDate.IsBetweenInclusive(fiveYearsAgo, fourYearsAgo)
				|| implantDate.IsBetweenInclusive(tenYearsAgo, nineYearsAgo))
			{
				inAYear++;
			}
		}
		return string.Format(GENESEED_FORMAT, currentGeneseed, inAYear);
	}

	private IReadOnlyList<Tuple<int, string>> GetSquadsWithInjuredSoldiers()
	{
		List<Squad> injuredSquads = [];
		foreach (Squad squad in GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle.GetAllSquads())
		{
			if (squad.Members.Any(s => s.Body.HitLocations.Any(hl => hl.IsSevered || hl.Wounds.WoundTotal > 0)))
			{
				injuredSquads.Add(squad);
			}
		}
		return injuredSquads.Select(s => new Tuple<int, string>(s.Id, GetSquadFullName(s))).ToList();
	}

	private string GetSquadFullName(Squad squad)
	{
		return $"{squad.Name}, ({squad.ParentUnit.Name})";
	}

	private void HandleSquadButtonPressed(object sender, int squadId)
	{
		Squad squad = GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle.GetAllSquads().First(s => s.Id == squadId);
		_view.PopulateInjuryDetail(GetSquadInjuryDetail(squad));
	}

	private string GetSquadInjuryDetail(Squad squad)
	{
		byte woundedSoldiers = 0;
		byte soldiersMissingBodyParts = 0;
		byte maxRecoveryTime = 0;
		byte unfitSoldiers = 0;
		foreach (ISoldier soldier in squad.Members)
		{
			bool isWounded = false;
			bool isMissingParts = false;
			bool isUnfit = false;
			byte greatestWoundHealTime = 0;
			foreach (HitLocation hitLocation in soldier.Body.HitLocations)
			{
				if (!isMissingParts && hitLocation.IsSevered)
				{
					isWounded = true;
					isMissingParts = true;
					isUnfit = true;
				}
				else if (hitLocation.Wounds.WoundTotal > 0)
				{
					isWounded = true;
					byte healTime = hitLocation.Wounds.RecoveryTimeLeft();
					if (healTime > greatestWoundHealTime)
					{
						greatestWoundHealTime = healTime;
					}
					if (hitLocation.IsCrippled)
					{
						isUnfit = true;
					}
				}
			}
			if (isWounded)
			{
				woundedSoldiers++;
			}
			if (isMissingParts)
			{
				soldiersMissingBodyParts++;
			}
			if (greatestWoundHealTime > maxRecoveryTime)
			{
				maxRecoveryTime = greatestWoundHealTime;
			}
			if (isUnfit)
			{
				unfitSoldiers++;
			}
		}
		if (woundedSoldiers == 0)
		{
			return squad.Name + " is entirely fit for duty.";
		}
		return string.Format(SQUAD_FORMAT,
							 squad.Name,
							 woundedSoldiers,
							 unfitSoldiers,
							 soldiersMissingBodyParts,
							 maxRecoveryTime);
	}
}
