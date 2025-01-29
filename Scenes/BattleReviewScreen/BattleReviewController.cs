using Godot;
using OnlyWar.Helpers;
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
		_currentTurn = 0;
		foreach (Node child in _spriteHolder.GetChildren())
		{
			_spriteHolder.RemoveChild(child);
			child.QueueFree();
		}

		_history = history;

		_view.SetTurnReportLabel("Battle Review: Turn 0");
		_view.SetTurnReportText("");
		_view.EnableTurnButtons(false, true);

		Vector2I topLeftOffset = GetTopLeftOfPositions(_history.StartingSoldierLocations.SelectMany(x => x.Value).ToList());
		foreach(Squad squad in _history.PlayerSquads)
		{
			DrawSquad(squad, _history.StartingSoldierLocations, topLeftOffset, _soldierTexture, _soldierTextureScale, squad.Faction.Color.ToGodotColor());
		}
		foreach(Squad squad in _history.OpposingSquads)
		{
			DrawSquad(squad, _history.StartingSoldierLocations, topLeftOffset, _opForTexture, _opForTextureScale, squad.Faction.Color.ToGodotColor());
		}
	}

	private void OnPreviousTurn()
	{
		if(_currentTurn < 2)
		{
			throw new InvalidOperationException("Cannot go back any further.");
		}
		int turn = _currentTurn - 1;
		LoadNewHistory(_history);
		IncrementToTurn(turn);
	}

	private void OnNextTurn()
	{
		IncrementToTurn(_currentTurn + 1);
	}

	private void IncrementToTurn(int turn)
	{
		_view.SetTurnReportLabel($"Battle Review: Turn {turn}");
		_view.EnableTurnButtons(turn > 1, turn < _history.Turns.Count);
		foreach (Node child in _spriteHolder.GetChildren())
		{
			_spriteHolder.RemoveChild(child);
			child.QueueFree();
		}
	}

	public Vector2I GetTopLeftOfPositions(IList<Tuple<int, int>> positions)
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

	private void DrawSquad(Squad squad, IReadOnlyDictionary<int, IList<Tuple<int, int>>> soldierLocationMap, Vector2I topLeftOffset, Texture2D soldierTexture, Vector2 soldierTextureScale, Color color)
	{
		foreach (ISoldier soldier in squad.Members)
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
