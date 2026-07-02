using Godot;

public partial class TacticalRegionView : Control
{
	[Export]
	public int RegionId { get; set; }
	private TextureRect _playerPublic, _playerHidden, _civilian, _xenos, _objective, _droppod;
	private RichTextLabel _playerPopulation, _civilianPopulation, _xenosPopulation, _regionName;
	private Button _button;
	private Color _tileColor = Colors.DarkSlateGray;
	private bool _selected;

	public override void _Ready()
	{
		_button = GetNode<Button>("Button");
		ConfigureTransparentButton();
		_regionName = GetNode<RichTextLabel>("Button/RegionNameLabel");
		_playerPublic = GetNode<TextureRect>("Button/TroopTexture");
		_playerHidden = GetNode<TextureRect>("Button/HiddenTroopTexture");
		_xenos = GetNode<TextureRect>("Button/XenosTexture");
		_civilian = GetNode<TextureRect>("Button/CivilianTexture");
		_objective = GetNode<TextureRect>("Button/ObjectiveTexture");
		_droppod = GetNode<TextureRect>("Button/DropPodTexture");
		_playerPopulation = GetNode<RichTextLabel>("Button/PlayerTroopCountLabel");
		_civilianPopulation = GetNode<RichTextLabel>("Button/CivilianPopulationLabel");
		_xenosPopulation = GetNode<RichTextLabel>("Button/XenosPopulationLabel");
		ConfigureText();
	}

	public override void _Draw()
	{
		Vector2 size = Size;
		if (size.X < 12f || size.Y < 12f) return;

		const float regularFlatTopRatio = 2f / 1.7320508f;
		float padding = _selected ? 3f : 5f;
		float hexWidth = Mathf.Min(size.X - padding * 2f, (size.Y - padding * 2f) * regularFlatTopRatio);
		float hexHeight = hexWidth / regularFlatTopRatio;
		Vector2 origin = new((size.X - hexWidth) * 0.5f, (size.Y - hexHeight) * 0.5f);
		float left = origin.X;
		float right = origin.X + hexWidth;
		float top = origin.Y;
		float bottom = origin.Y + hexHeight;
		float middleY = origin.Y + hexHeight * 0.5f;
		Vector2[] points =
		[
			new Vector2(origin.X + hexWidth * 0.25f, top),
			new Vector2(origin.X + hexWidth * 0.75f, top),
			new Vector2(right, middleY),
			new Vector2(origin.X + hexWidth * 0.75f, bottom),
			new Vector2(origin.X + hexWidth * 0.25f, bottom),
			new Vector2(left, middleY)
		];

		Color fill = _selected ? _tileColor.Lightened(0.16f) : _tileColor;
		fill.A = _selected ? 0.92f : 0.78f;
		Color[] fillColors = new Color[points.Length];
		System.Array.Fill(fillColors, fill);
		DrawPolygon(points, fillColors);

		Color radial = new Color(0.96f, 0.84f, 0.52f, _selected ? 0.22f : 0.075f);
		Vector2 center = size * 0.5f;
		foreach (Vector2 point in points)
		{
			DrawLine(center, point, radial, 1f);
		}

		Color border = _selected ? new Color(0.96f, 0.84f, 0.52f, 1f) : new Color(0.55f, 0.43f, 0.25f, 0.62f);
		Vector2[] outline =
		[
			points[0],
			points[1],
			points[2],
			points[3],
			points[4],
			points[5],
			points[0]
		];
		DrawPolyline(outline, border, _selected ? 3f : 1.4f);
	}

	public void Populate(int regionId, string name, bool showPlayerPublic, bool showPlayerHidden, bool showCivilian, bool showXenos, bool showObjective, bool showDropPod,
		string playerPopulation, string civilianPopulation, string xenosPopulation, Color color, bool selected)
	{
		RegionId = regionId;
		_tileColor = color;
		_selected = selected;
		_regionName.Text = name;
		_playerPublic.Visible = showPlayerPublic;
		_playerHidden.Visible = showPlayerHidden;
		_civilian.Visible = showCivilian;
		_xenos.Visible = showXenos;
		_objective.Visible = showObjective;
		_droppod.Visible = showDropPod;
		_playerPopulation.Text = playerPopulation;
		_civilianPopulation.Text = civilianPopulation;
		_xenosPopulation.Text = xenosPopulation;
		_button.ButtonPressed = selected;
		QueueRedraw();
    }

	public void SetRegionBackgroundColor(Color color)
    {
		_tileColor = color;
		QueueRedraw();
    }

	private void ConfigureTransparentButton()
	{
		StyleBoxEmpty empty = new();
		_button.AddThemeStyleboxOverride("normal", empty);
		_button.AddThemeStyleboxOverride("hover", empty);
		_button.AddThemeStyleboxOverride("pressed", empty);
		_button.AddThemeStyleboxOverride("focus", empty);
		_button.AddThemeStyleboxOverride("disabled", empty);
		_button.SelfModulate = Colors.White;
	}

	private void ConfigureText()
	{
		_regionName.AddThemeFontSizeOverride("normal_font_size", 13);
		_regionName.AddThemeColorOverride("default_color", new Color(0.88f, 0.83f, 0.72f));
		foreach (RichTextLabel label in new[] { _playerPopulation, _civilianPopulation, _xenosPopulation })
		{
			label.AddThemeFontSizeOverride("normal_font_size", 11);
			label.AddThemeColorOverride("default_color", new Color(0.76f, 0.72f, 0.62f));
		}
	}
}
