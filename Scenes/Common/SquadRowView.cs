using Godot;
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
    private bool _borderless;
    private bool _strengthVisible = true;
    private bool _redundantStatusTokensHidden;
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
        _strengthLabel.Visible = _strengthVisible;
        _strengthLabel.AddThemeColorOverride(
            "font_color",
            model.Enabled ? OnlyWarStyle.BodyText : OnlyWarStyle.MutedText);
        _secondaryLabel.Text = SecondaryText(model, _redundantStatusTokensHidden);
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

    /// <summary>
    /// Hides this row's panel frame while preserving its content margins. This is used when a
    /// squad row is embedded inside another list row that already owns the visual frame.
    /// </summary>
    public void SetBorderless(bool borderless = true)
    {
        _borderless = borderless;
        ApplyVisualState();
    }

    /// <summary>
    /// Hides the row's strength readout when the host provides a dedicated strength column.
    /// </summary>
    public void SetStrengthVisible(bool visible)
    {
        _strengthVisible = visible;
        if (_strengthLabel != null && IsInstanceValid(_strengthLabel))
        {
            _strengthLabel.Visible = visible;
        }
    }

    /// <summary>
    /// Hides vacancy/leader tokens that are already represented by the host's group headings.
    /// Other readiness blockers remain visible and the complete readiness facts remain in the
    /// row tooltip.
    /// </summary>
    public void SetRedundantStatusTokensHidden(bool hidden = true)
    {
        _redundantStatusTokensHidden = hidden;
        if (_model != null && _secondaryLabel != null && IsInstanceValid(_secondaryLabel))
        {
            _secondaryLabel.Text = SecondaryText(_model, hidden);
        }
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
        if (_borderless)
        {
            style.BgColor = Colors.Transparent;
            style.BorderColor = Colors.Transparent;
            style.BorderWidthLeft = 0;
            style.BorderWidthTop = 0;
            style.BorderWidthRight = 0;
            style.BorderWidthBottom = 0;
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

    private static string SecondaryText(
        SquadRowViewModel model,
        bool redundantStatusTokensHidden = false)
    {
        List<string> tokens = [];
        if (!string.IsNullOrWhiteSpace(model.Type))
        {
            tokens.Add(model.Type);
        }
        if (model is BattleSquadRowViewModel battle)
        {
            // Replay rows carry no campaign location, leader or readiness state: those facts
            // describe the squad after the battle, not the turn on screen.
            if (!string.IsNullOrWhiteSpace(battle.MoraleLabel)) tokens.Add(battle.MoraleLabel);
            if (!string.IsNullOrWhiteSpace(battle.FatigueLabel)) tokens.Add(battle.FatigueLabel);
            return string.Join(" · ", tokens);
        }
        if (!string.IsNullOrWhiteSpace(model.Location))
        {
            tokens.Add(model.Location);
        }
        if (!string.IsNullOrWhiteSpace(model.PrimaryStateLabel)
            && (!redundantStatusTokensHidden || !IsRedundantStatus(model.Readiness.PrimaryBlocker)))
        {
            tokens.Add(model.PrimaryStateLabel);
        }
        if (!string.IsNullOrWhiteSpace(model.LeaderLabel)
            && model.PrimaryStateLabel != model.LeaderLabel
            && (!redundantStatusTokensHidden || model.LeaderStatus != SquadLeaderStatus.Vacant))
        {
            tokens.Add(model.LeaderLabel);
        }
        tokens.Add(model.CommitmentLabel);
        if (!string.IsNullOrWhiteSpace(model.ContextBadge))
        {
            tokens.Add(model.ContextBadge);
        }
        return string.Join(" · ", tokens);
    }

    private static bool IsRedundantStatus(SquadReadinessBlocker blocker) =>
        blocker == SquadReadinessBlocker.Leaderless
        || blocker == SquadReadinessBlocker.BelowMinimumDutyReadyStrength;

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
