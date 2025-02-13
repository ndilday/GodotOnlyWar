using Godot;

public partial class TacticalRegionView : Control
{
	[Export]
	public int RegionId { get; set; }
	private TextureRect _playerPublic, _playerHidden, _civilian, _xenos, _objective, _droppod;
	private RichTextLabel _playerPopulation, _civilianPopulation, _xenosPopulation, _regionName;

	public override void _Ready()
	{
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
	}

	public void Populate(int regionId, string name, bool showPlayerPublic, bool showPlayerHidden, bool showCivilian, bool showXenos, bool showObjective, bool showDropPod,
		string playerPopulation, string civilianPopulation, string xenosPopulation)
	{
		RegionId = regionId;
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
	}
}
