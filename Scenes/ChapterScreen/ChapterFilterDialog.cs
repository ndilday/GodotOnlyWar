using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;
using System.Linq;

// A Football-Manager-style filter builder: a stack of condition rows (field + operator +
// value) combined with AND. Opened from the Chapter screen's left-menu Filter button and
// populated with the roles / honors available in the current browse scope.
public partial class ChapterFilterDialog : AcceptDialog
{
    private static readonly Vector2I DialogMinimumSize = new(880, 360);
    private static readonly Vector2 ContentMinimumSize = new(856, 320);

    private static readonly SoldierFilterField[] FieldOrder =
    [
        SoldierFilterField.Rank,
        SoldierFilterField.Honor,
        SoldierFilterField.TimeInService,
        SoldierFilterField.TimeInRank,
        SoldierFilterField.TimeInSquad
    ];

    private static string FieldLabel(SoldierFilterField field) => field switch
    {
        SoldierFilterField.Rank => "Rank / role",
        SoldierFilterField.Honor => "Honor",
        SoldierFilterField.TimeInService => "Time in service",
        SoldierFilterField.TimeInRank => "Time in rank",
        SoldierFilterField.TimeInSquad => "Time in squad",
        _ => field.ToString()
    };

    private VBoxContainer _rowsContainer;
    private readonly List<ConditionRow> _rows = [];
    private IReadOnlyList<string> _roles = [];
    private IReadOnlyList<SoldierHonorFilterOption> _honors = [];

    public event Action<List<SoldierFilterCondition>> FilterApplied;
    public event Action FilterCleared;

    public override void _Ready()
    {
        Title = "Filter Battle Brothers";
        OkButtonText = "Apply";
        MinSize = DialogMinimumSize;
        Size = DialogMinimumSize;
        Unresizable = false;

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        margin.CustomMinimumSize = ContentMinimumSize;
        AddChild(margin);

        VBoxContainer root = new();
        root.AddThemeConstantOverride("separation", 8);
        margin.AddChild(root);

        Label header = new()
        {
            Text = "Show battle brothers matching ALL of these conditions:"
        };
        header.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        root.AddChild(header);

        ScrollContainer scroll = new()
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        root.AddChild(scroll);

        _rowsContainer = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _rowsContainer.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_rowsContainer);

        Button addButton = new()
        {
            Text = "+ Add condition",
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand
        };
        addButton.Pressed += () => AddRow(null);
        root.AddChild(addButton);

        // Right-side "Clear" button beside the built-in Apply/OK button.
        AddButton("Clear", true, "clear");
        CustomAction += OnCustomAction;
        Confirmed += OnConfirmed;
    }

    // Refreshes the option lists from the current scope and rebuilds the rows to match the
    // active filter (or a single blank row when there is none).
    public void Populate(IReadOnlyList<string> roles, IReadOnlyList<SoldierHonorFilterOption> honors,
                         IReadOnlyList<SoldierFilterCondition> existing)
    {
        _roles = roles ?? [];
        _honors = honors ?? [];

        foreach (ConditionRow row in _rows)
        {
            row.Root.QueueFree();
        }
        _rows.Clear();

        if (existing != null && existing.Count > 0)
        {
            foreach (SoldierFilterCondition condition in existing)
            {
                AddRow(condition);
            }
        }
        else
        {
            AddRow(null);
        }
    }

    private void AddRow(SoldierFilterCondition seed)
    {
        ConditionRow row = new(_roles, _honors, seed, RemoveRow);
        _rows.Add(row);
        _rowsContainer.AddChild(row.Root);
    }

    private void RemoveRow(ConditionRow row)
    {
        _rows.Remove(row);
        row.Root.QueueFree();
    }

    private void OnConfirmed()
    {
        List<SoldierFilterCondition> conditions = _rows
            .Select(row => row.ReadCondition())
            .Where(condition => condition != null)
            .ToList();
        FilterApplied?.Invoke(conditions);
    }

    private void OnCustomAction(StringName action)
    {
        if (action == "clear")
        {
            FilterCleared?.Invoke();
            Hide();
        }
    }

    // One editable condition. The field selector rebuilds the operator options and swaps the
    // value control (role/honor dropdown vs. a duration amount + unit) whenever it changes.
    private sealed class ConditionRow
    {
        private static readonly (SoldierFilterOperator Op, string Label)[] EqualityOps =
        [
            (SoldierFilterOperator.Equals, "is"),
            (SoldierFilterOperator.NotEquals, "is not")
        ];
        private static readonly (SoldierFilterOperator Op, string Label)[] RankOps =
        [
            (SoldierFilterOperator.Equals, "is"),
            (SoldierFilterOperator.NotEquals, "is not"),
            (SoldierFilterOperator.Below, "below"),
            (SoldierFilterOperator.Above, "above")
        ];
        private static readonly (SoldierFilterOperator Op, string Label)[] HonorOps =
        [
            (SoldierFilterOperator.Has, "has at least"),
            (SoldierFilterOperator.DoesNotHave, "does not have")
        ];
        private static readonly (SoldierFilterOperator Op, string Label)[] DurationOps =
        [
            (SoldierFilterOperator.AtLeast, "at least"),
            (SoldierFilterOperator.AtMost, "at most")
        ];

        private readonly IReadOnlyList<string> _roles;
        private readonly IReadOnlyList<SoldierHonorFilterOption> _honors;

        private readonly OptionButton _fieldOption = new();
        private readonly OptionButton _operatorOption = new();
        private readonly Container _valueSlot;

        private OptionButton _valueOption;
        private SpinBox _amountSpin;
        private OptionButton _unitOption;

        private (SoldierFilterOperator Op, string Label)[] _currentOps = EqualityOps;

        public HBoxContainer Root { get; }

        public ConditionRow(IReadOnlyList<string> roles, IReadOnlyList<SoldierHonorFilterOption> honors,
                            SoldierFilterCondition seed, Action<ConditionRow> onRemove)
        {
            _roles = roles;
            _honors = honors;

            Root = new HBoxContainer();
            Root.AddThemeConstantOverride("separation", 6);
            Root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            _fieldOption.CustomMinimumSize = new Vector2(150, 0);
            foreach (SoldierFilterField field in FieldOrder)
            {
                _fieldOption.AddItem(FieldLabel(field));
            }
            Root.AddChild(_fieldOption);

            _operatorOption.CustomMinimumSize = new Vector2(120, 0);
            Root.AddChild(_operatorOption);

            _valueSlot = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            _valueSlot.AddThemeConstantOverride("separation", 6);
            Root.AddChild(_valueSlot);

            Button removeButton = new()
            {
                Text = "X",
                CustomMinimumSize = new Vector2(32, 0),
                TooltipText = "Remove this condition",
                MouseDefaultCursorShape = Control.CursorShape.PointingHand
            };
            removeButton.Pressed += () => onRemove(this);
            Root.AddChild(removeButton);

            int fieldIndex = seed == null ? 0 : Array.IndexOf(FieldOrder, seed.Field);
            _fieldOption.Selected = Math.Max(0, fieldIndex);
            _fieldOption.ItemSelected += _ => RebuildForField(null);
            RebuildForField(seed);
        }

        private SoldierFilterField SelectedField => FieldOrder[_fieldOption.Selected];

        private void RebuildForField(SoldierFilterCondition seed)
        {
            SoldierFilterField field = SelectedField;

            _currentOps = field switch
            {
                SoldierFilterField.Rank => RankOps,
                SoldierFilterField.Honor => HonorOps,
                _ => DurationOps
            };
            _operatorOption.Clear();
            foreach ((_, string label) in _currentOps)
            {
                _operatorOption.AddItem(label);
            }
            int opIndex = seed == null ? 0 : Array.FindIndex(_currentOps, o => o.Op == seed.Operator);
            _operatorOption.Selected = Math.Max(0, opIndex);

            foreach (Node child in _valueSlot.GetChildren())
            {
                child.QueueFree();
            }
            _valueOption = null;
            _amountSpin = null;
            _unitOption = null;

            if (SoldierFilterCondition.IsDurationField(field))
            {
                BuildDurationValue(seed);
            }
            else
            {
                BuildChoiceValue(field, seed);
            }
        }

        private void BuildChoiceValue(SoldierFilterField field, SoldierFilterCondition seed)
        {
            _valueOption = new OptionButton
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            int optionCount = field == SoldierFilterField.Rank ? _roles.Count : _honors.Count;
            if (optionCount == 0)
            {
                _valueOption.AddItem(field == SoldierFilterField.Rank ? "(no roles here)" : "(no honors here)");
                _valueOption.Disabled = true;
            }
            else if (field == SoldierFilterField.Honor)
            {
                foreach (SoldierHonorFilterOption option in _honors)
                {
                    _valueOption.AddItem(option.Label);
                }
                if (seed?.TextValue != null)
                {
                    int idx = _honors.ToList().FindIndex(option =>
                        option.Value == seed.TextValue || option.Type == seed.TextValue);
                    if (idx >= 0)
                    {
                        _valueOption.Selected = idx;
                    }
                }
            }
            else
            {
                IReadOnlyList<string> options = _roles;
                foreach (string option in options)
                {
                    _valueOption.AddItem(option);
                }
                if (seed?.TextValue != null)
                {
                    int idx = options.ToList().IndexOf(seed.TextValue);
                    if (idx >= 0)
                    {
                        _valueOption.Selected = idx;
                    }
                }
            }
            _valueSlot.AddChild(_valueOption);
        }

        private void BuildDurationValue(SoldierFilterCondition seed)
        {
            _amountSpin = new SpinBox
            {
                MinValue = 0,
                MaxValue = 9999,
                Step = 1,
                Value = seed?.NumberValue ?? 1,
                CustomMinimumSize = new Vector2(90, 0)
            };
            _valueSlot.AddChild(_amountSpin);

            _unitOption = new OptionButton
            {
                CustomMinimumSize = new Vector2(90, 0)
            };
            _unitOption.AddItem("years");   // index 0 -> Years
            _unitOption.AddItem("weeks");   // index 1 -> Weeks
            _unitOption.Selected = seed?.Unit == SoldierDurationUnit.Weeks ? 1 : 0;
            _valueSlot.AddChild(_unitOption);
        }

        public SoldierFilterCondition ReadCondition()
        {
            SoldierFilterField field = SelectedField;
            SoldierFilterOperator op = _currentOps[Math.Max(0, _operatorOption.Selected)].Op;

            if (SoldierFilterCondition.IsDurationField(field))
            {
                return new SoldierFilterCondition
                {
                    Field = field,
                    Operator = op,
                    NumberValue = (int)_amountSpin.Value,
                    Unit = _unitOption.Selected == 1 ? SoldierDurationUnit.Weeks : SoldierDurationUnit.Years
                };
            }
            // Choice fields with no available options (or nothing selected) are ignored.
            if (_valueOption == null || _valueOption.Disabled || _valueOption.Selected < 0)
            {
                return null;
            }

            return new SoldierFilterCondition
            {
                Field = field,
                Operator = op,
                TextValue = field == SoldierFilterField.Honor
                    ? _honors[_valueOption.Selected].Value
                    : _valueOption.GetItemText(_valueOption.Selected)
            };
        }
    }
}
