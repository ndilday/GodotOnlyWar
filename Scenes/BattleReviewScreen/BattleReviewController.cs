using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Scenes.MainGameScreen;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class BattleReviewController : DialogController
{
	private BattleReviewView _view;
	private BattleHistory _history;
	private Texture2D _soldierTexture, _opForTexture;
	private Node _spriteHolder;
	private Vector2 _soldierTextureScale, _opForTextureScale;
	private readonly Vector2I GRID_BORDER_SIZE = Vector2I.One;
	private Vector2I _pixelsPerGrid;
	private ushort _currentTurn;

	public override void _Ready()
	{
		base._Ready();
		_view = GetNode<BattleReviewView>("DialogView");
		_view.PreviousTurnPressed += (object sender, EventArgs e) => OnPreviousTurn();
		_view.NextTurnPressed += (object sender, EventArgs e) => OnNextTurn();
		_pixelsPerGrid = new(GameDataSingleton.Instance.GameRulesData.BattleCellSize.Item1, GameDataSingleton.Instance.GameRulesData.BattleCellSize.Item2);
		_soldierTexture = (Texture2D)GD.Load("res://Assets/helmet.png");
		Vector2 floatingVector = new Vector2(_pixelsPerGrid.X, _pixelsPerGrid.Y);
		_soldierTextureScale = (floatingVector - GRID_BORDER_SIZE) / _soldierTexture.GetSize();
		_opForTexture = (Texture2D)GD.Load("res://Assets/objective_icon.png");
		_opForTextureScale = (floatingVector - GRID_BORDER_SIZE) / _opForTexture.GetSize();
		_spriteHolder = GetNode<Node>("SpriteHolder");
	}

	public void LoadNewHistory(BattleHistory history)
	{
        _history = history;
		DisplayTurn(0);
	}

	private void OnPreviousTurn()
	{
		if(_currentTurn < 1)
		{
			throw new InvalidOperationException("Cannot go back any further.");
		}
		DisplayTurn(_currentTurn - 1);
	}

	private void OnNextTurn()
	{
		DisplayTurn(_currentTurn + 1);
	}

	private void DisplayTurn(int turn)
	{
        _currentTurn = (ushort)turn;

        _view.SetTurnReportLabel("Battle Review: Turn 0");
		string turnReport = "";
		foreach(IAction action in _history.Turns[_currentTurn].Actions.OrderByDescending(a => a.ActorId))
		{
            turnReport += action.Description() + "\n";
			if (action is ShootAction shootAction)
			{
				foreach(WoundResolution wound in shootAction.WoundResolutions)
                {
                    turnReport += wound.Description + "\n";
                }
            }
			else if (action is MeleeAttackAction meleeAction)
			{
                foreach (WoundResolution wound in meleeAction.WoundResolutions)
                {
                    turnReport += wound.Description + "\n";
                }
            }
        }
        _view.SetTurnReportText(turnReport);
        _view.EnableTurnButtons(turn > 1, turn < _history.Turns.Count);

        foreach (Node child in _spriteHolder.GetChildren())
        {
            _spriteHolder.RemoveChild(child);
            child.QueueFree();
        }

        Vector2I topLeftOffset = GetTopLeftOfPositions(_history.Turns[0].State.SoldierPositionsMap.Values.SelectMany(x => x).ToList());
        foreach (BattleSquad squad in _history.Turns[_currentTurn].State.PlayerSquads.Values)
        {
            DrawSquad(squad, _history.Turns[_currentTurn].State.SoldierPositionsMap, topLeftOffset, _soldierTexture, _soldierTextureScale, squad.Squad.Faction.Color.ToGodotColor());
        }
        foreach (BattleSquad squad in _history.Turns[_currentTurn].State.OpposingSquads.Values)
        {
            DrawSquad(squad, _history.Turns[_currentTurn].State.SoldierPositionsMap, topLeftOffset, _opForTexture, _opForTextureScale, squad.Squad.Faction.Color.ToGodotColor());
        }

    }

	public Vector2I GetTopLeftOfPositions(IReadOnlyList<Tuple<int, int>> positions)
	{
		return new 
			(
				positions.Min(x => x.Item1),
				positions.Min(x => x.Item2)
			);
	}

	public Vector2I GetBottomRightOfPositions(IList<Tuple<int, int>> positions)
	{
		return new
			(
				positions.Max(x => x.Item1),
				positions.Max(x => x.Item2)
			);
	}

	private void DrawSquad(BattleSquad squad, IReadOnlyDictionary<int, IReadOnlyList<Tuple<int, int>>> soldierLocationMap, Vector2I topLeftOffset, Texture2D soldierTexture, Vector2 soldierTextureScale, Color color)
	{
		foreach (ISoldier soldier in squad.Soldiers)
		{
			var soldierLocations = soldierLocationMap[soldier.Id];
			if(soldierLocations.Count == 1)
			{
				Vector2I gridPosition = new Vector2I(soldierLocations[0].Item1, soldierLocations[0].Item2);
				Vector2I adjustedPosition = gridPosition - topLeftOffset + GRID_BORDER_SIZE;
				Vector2I pixelPosition = (adjustedPosition * _pixelsPerGrid) + GRID_BORDER_SIZE;
				DrawTexture(soldierTexture, soldierTextureScale, pixelPosition, color);
			}
		}
	}

	private ClickableSprite2D DrawTexture(Texture2D texture, Vector2 scale, Vector2I pixelPosition, Color color, int zIndex = 1, bool offset = false)
	{
		ClickableSprite2D newSprite = new ClickableSprite2D();
		_spriteHolder.AddChild(newSprite);
		newSprite.Owner = _spriteHolder;
		newSprite.GlobalPosition = pixelPosition;
		newSprite.Texture = texture;
		newSprite.Modulate = color;
		newSprite.Scale = scale;
		newSprite.ZIndex = zIndex;
		return newSprite;
	}
}
