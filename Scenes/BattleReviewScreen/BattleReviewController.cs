using Godot;
using OnlyWar.Models.Battles;
using System;

public partial class BattleReviewController : DialogController
{
	private BattleReviewView _view;
	private BattleHistory _history;

	public override void _Ready()
	{
		base._Ready();
		_view = GetNode<BattleReviewView>("DialogView");
	}

	public void LoadNewHistory(BattleHistory history)
	{
		_history = history;
		//_view.LoadNewHistory(history);
	}
}
