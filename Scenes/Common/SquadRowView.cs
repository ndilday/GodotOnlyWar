using Godot;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;

/// <summary>
/// The single live-squad row renderer. Hosts provide navigation and mutations; this component
/// owns the common icon, two-line typography, strength, state tokens, focus, and tooltip.
/// </summary>
public partial class SquadRowView : PanelContainer
{
    private const int DefaultRowHeight = 46;
    private const int IconSize = 30;
    private SquadRowViewModel _model;
    private Label _nameLabel;
    private Label _strengthLabel;
    private Label _secondaryLabel;
    private TextureRect _icon;
    private bool _selected;
    private bool _hovered;
    private int _indent;

    public event EventHandler<string> RowSelected;
    public event EventHandler<string> RowActivated;

    public SquadRowViewModel Model => _model;

    public void SetIndent(int indent)
    {
        _indent = Math.Max(0, indent);
        ApplyVisualState();
    }

    public void Configure(SquadRowViewModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        EnsureBuilt();
        _selected = model.Selected;
        TooltipText = model.Tooltip;
        CustomMinimumSize = new Vector2(0, DefaultRowHeight);
        FocusMode = model.Selectable ? FocusModeEnum.All : FocusModeEnum.None;
        MouseDefaultCursorShape = model.Selectable
            ? CursorShape.PointingHand
            : CursorShape.Arrow;
        _icon.Texture = IconAtlas.GetIcon(model.IconKey);
        _icon.Visible = !string.IsNullOrWhiteSpace(model.IconKey);
        _nameLabel.Text = model.Name;
        _nameLabel.AddThemeColorOverride(
            "font_color",
            model.Enabled ? OnlyWarStyle.BodyText : OnlyWarStyle.MutedText);
        _strengthLabel.Text = StrengthText(model);
        _strengthLabel.AddThemeColorOverride(
            "font_color",
            model.Enabled ? OnlyWarStyle.BodyText : OnlyWarStyle.MutedText);
        _secondaryLabel.Text = SecondaryText(model);
        _secondaryLabel.AddThemeColorOverride("font_color", SecondaryColor(model));
        ApplyVisualState();
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        ApplyVisualState();
    }

    public void SetHovered(bool hovered)
    {
        _hovered = hovered;
        ApplyVisualState();
    }

    public override void _Ready()
    {
        EnsureBuilt();
    }

    private void EnsureBuilt()
    {
        if (_nameLabel != null && IsInstanceValid(_nameLabel))
        {
            return;
        }

        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
        HBoxContainer content = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        content.AddThemeConstantOverride("separation", 7);
        AddChild(content);

        _icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(IconSize, IconSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore
        };
        content.AddChild(_icon);

        VBoxContainer textStack = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore
        };
        textStack.AddThemeConstantOverride("separation", 0);
        content.AddChild(textStack);

        _nameLabel = new Label
        {
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        textStack.AddChild(_nameLabel);

        _secondaryLabel = new Label
        {
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _secondaryLabel.AddThemeFontSizeOverride("font_size", 11);
        textStack.AddChild(_secondaryLabel);

        _strengthLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
            CustomMinimumSize = new Vector2(54, 0),
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = MouseFilterEnum.Ignore
        };
        content.AddChild(_strengthLabel);

        MouseEntered += () => SetHovered(true);
        MouseExited += () => SetHovered(false);
        GuiInput += OnGuiInput;
    }

    private void OnGuiInput(InputEvent inputEvent)
    {
        if (_model == null || !_model.Selectable)
        {
            return;
        }

        if (inputEvent is InputEventMouseButton mouse
            && mouse.ButtonIndex == MouseButton.Left
            && mouse.Pressed)
        {
            RowSelected?.Invoke(this, _model.Key);
            if (mouse.DoubleClick)
            {
                RowActivated?.Invoke(this, _model.Key);
            }
            AcceptEvent();
            return;
        }

        if (inputEvent is InputEventKey key
            && key.Pressed
            && !key.Echo
            && (key.Keycode == Key.Enter || key.Keycode == Key.KpEnter
                || key.Keycode == Key.Space))
        {
            RowSelected?.Invoke(this, _model.Key);
            RowActivated?.Invoke(this, _model.Key);
            AcceptEvent();
        }
    }

    private void ApplyVisualState()
    {
        if (_model == null)
        {
            return;
        }

        StyleBoxFlat style = OnlyWarStyle.GetListRowStyle(_selected || _hovered || HasFocus());
        style.ContentMarginLeft = 6 + _indent;
        style.ContentMarginTop = 3;
        style.ContentMarginRight = 6;
        style.ContentMarginBottom = 3;
        if (!_model.Enabled)
        {
            style.BgColor = OnlyWarStyle.WithAlpha(style.BgColor, 0.48f);
            style.BorderColor = OnlyWarStyle.WithAlpha(style.BorderColor, 0.42f);
        }
        AddThemeStyleboxOverride("panel", style);
    }

    private static string StrengthText(SquadRowViewModel model)
    {
        if (model is BattleSquadRowViewModel battle)
        {
            return $"{battle.CurrentStrength}/{battle.StartingStrength}";
        }
        if (model is ProjectedSquadRowViewModel projected)
        {
            string delta = projected.OutgoingDelta > 0
                ? $" -{projected.OutgoingDelta}"
                : string.Empty;
            if (projected.IncomingDelta > 0)
            {
                delta += $" +{projected.IncomingDelta}";
            }
            return $"{projected.FutureStrength}/{projected.Strength.Full}{delta}";
        }
        return model.StrengthLabel;
    }

    private static string SecondaryText(SquadRowViewModel model)
    {
        List<string> tokens = [];
        if (!string.IsNullOrWhiteSpace(model.Type))
        {
            tokens.Add(model.Type);
        }
        if (!string.IsNullOrWhiteSpace(model.Location))
        {
            tokens.Add(model.Location);
        }
        if (!string.IsNullOrWhiteSpace(model.PrimaryStateLabel))
        {
            tokens.Add(model.PrimaryStateLabel);
        }
        if (!string.IsNullOrWhiteSpace(model.LeaderLabel)
            && model.PrimaryStateLabel != model.LeaderLabel)
        {
            tokens.Add(model.LeaderLabel);
        }
        if (model is BattleSquadRowViewModel battle)
        {
            tokens.Add("HISTORICAL");
            if (!string.IsNullOrWhiteSpace(battle.MoraleLabel)) tokens.Add(battle.MoraleLabel);
            if (!string.IsNullOrWhiteSpace(battle.FatigueLabel)) tokens.Add(battle.FatigueLabel);
        }
        else
        {
            tokens.Add(model.CommitmentLabel);
        }
        if (!string.IsNullOrWhiteSpace(model.ContextBadge))
        {
            tokens.Add(model.ContextBadge);
        }
        return string.Join(" · ", tokens);
    }

    private static Color SecondaryColor(SquadRowViewModel model)
    {
        if (model.Readiness.PrimaryBlocker == SquadReadinessBlocker.None)
        {
            return OnlyWarStyle.MutedText;
        }
        return model.Enabled
            ? OnlyWarStyle.MedicalWarning
            : OnlyWarStyle.MutedText;
    }
}
