using Godot;

public partial class WeaponSetRowView : Control
{
	private RichTextLabel _weaponSetNameLabel;
	private SpinBox _weaponCount;
	[Export]
	public int MinimumCount { get => (int)_weaponCount.MinValue; set => _weaponCount.MinValue = value; }
	[Export]
	public int MaximumCount { get => (int)_weaponCount.MaxValue; set => _weaponCount.MaxValue = value; }
	public int Count { get => (int)_weaponCount.Value; }
	public override void _Ready()
	{
		_weaponSetNameLabel = GetNode<RichTextLabel>("WeaponSetNameLabel");
		_weaponCount = GetNode<SpinBox>("WeaponCount");
	}

	public void SetCount(int count)
	{
		_weaponCount.Value = count;
	}

	public void SetWeaponSetName(string name)
	{
		_weaponSetNameLabel.Text = name;
	}
}
