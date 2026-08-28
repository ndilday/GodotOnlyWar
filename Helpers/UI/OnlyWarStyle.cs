using Godot;

namespace OnlyWar.Helpers.UI
{
    public enum OnlyWarEventTone
    {
        Normal,
        Warning,
        Critical
    }

    public static class OnlyWarStyle
    {
        private const string ThemePath = "res://Scenes/OnlyWarTheme.tres";
        private const string ContentPanelType = "OnlyWarContentPanel";
        private const string InsetPanelType = "OnlyWarInsetPanel";
        private const string ListRowType = "OnlyWarListRow";
        private const string EventPanelType = "OnlyWarEventPanel";

        private static Theme _theme;

        public static readonly Color Gold = new(0.96f, 0.84f, 0.52f);
        public static readonly Color MutedText = new(0.66f, 0.60f, 0.49f);
        public static readonly Color BodyText = new(0.84f, 0.80f, 0.71f);
        public static readonly Color PlayerAccent = Color.Color8(99, 199, 215);
        public static readonly Color OpposingAccent = Color.Color8(204, 83, 71);
        public static readonly Color MapGrid = Color.Color8(124, 102, 59, 55);
        public static readonly Color MapBackground = Color.Color8(10, 12, 13);
        public static readonly Color MapContested = Color.Color8(255, 140, 26);
        public static readonly Color MedicalStable = new(0.34f, 0.64f, 0.37f);
        public static readonly Color MedicalWarning = new(0.83f, 0.63f, 0.31f);
        public static readonly Color Critical = new(0.92f, 0.28f, 0.20f);
        public static readonly Color IntelligenceNone = Color.Color8(211, 47, 47);
        public static readonly Color IntelligenceBasic = Color.Color8(230, 74, 25);
        public static readonly Color IntelligenceLimited = Color.Color8(255, 179, 0);
        public static readonly Color IntelligencePartial = Color.Color8(224, 224, 224);
        public static readonly Color IntelligenceReliable = Color.Color8(174, 213, 129);
        public static readonly Color IntelligenceDetailed = Color.Color8(102, 187, 106);
        public static readonly Color IntelligenceComprehensive = Color.Color8(56, 142, 60);

        public static Color GetIntelligenceColor(string level) => level switch
        {
            "None" => IntelligenceNone,
            "Basic" => IntelligenceBasic,
            "Limited" => IntelligenceLimited,
            "Partial" => IntelligencePartial,
            "Reliable" => IntelligenceReliable,
            "Detailed" => IntelligenceDetailed,
            "Comprehensive" => IntelligenceComprehensive,
            _ => BodyText
        };

        public static Color WithAlpha(Color color, float alpha)
        {
            color.A = alpha;
            return color;
        }

        public static void ApplyContentPanel(PanelContainer panel)
        {
            panel.AddThemeStyleboxOverride("panel", GetSharedStylebox("panel", ContentPanelType));
        }

        public static void ApplyContentPanel(Panel panel)
        {
            panel.AddThemeStyleboxOverride("panel", GetSharedStylebox("panel", ContentPanelType));
        }

        public static void ApplyInsetPanel(PanelContainer panel)
        {
            panel.AddThemeStyleboxOverride("panel", GetSharedStylebox("panel", InsetPanelType));
        }

        public static void ApplyInsetPanel(Panel panel)
        {
            panel.AddThemeStyleboxOverride("panel", GetSharedStylebox("panel", InsetPanelType));
        }

        /// <summary>
        /// Applies the quiet, recessed treatment intended for read-only information.
        /// Unlike a button, this surface does not advertise hover or press affordance.
        /// </summary>
        public static void ApplyDataPanel(PanelContainer panel)
        {
            panel.AddThemeStyleboxOverride("panel", GetDataPanelStyle());
        }

        public static void ApplyDataPanel(Panel panel)
        {
            panel.AddThemeStyleboxOverride("panel", GetDataPanelStyle());
        }

        /// <summary>
        /// Applies the raised, responsive treatment intended for an action control.
        /// The hover and pressed states carry most of the visual affordance.
        /// </summary>
        public static void ApplyActionButton(Button button, Color? accent = null)
        {
            Color actionAccent = accent ?? Gold;
            button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            button.AddThemeStyleboxOverride("normal", CreateActionButtonStyle(
                new Color(0.055f, 0.060f, 0.061f, 0.98f),
                new Color(actionAccent.R, actionAccent.G, actionAccent.B, 0.72f),
                6));
            button.AddThemeStyleboxOverride("hover", CreateActionButtonStyle(
                new Color(0.16f, 0.125f, 0.060f, 0.99f),
                actionAccent,
                7));
            button.AddThemeStyleboxOverride("pressed", CreateActionButtonStyle(
                new Color(0.28f, 0.19f, 0.065f, 1.0f),
                actionAccent.Lightened(0.08f),
                8));
            button.AddThemeStyleboxOverride("disabled", CreateActionButtonStyle(
                new Color(0.025f, 0.028f, 0.029f, 0.68f),
                WithAlpha(MutedText, 0.30f),
                6));
        }

        /// <summary>
        /// A deliberately obvious, skeuomorphic action control for high-priority commands.
        /// The rounded silhouette and shadow separate it from every read-only surface.
        /// </summary>
        public static void ApplyRaisedButton(Button button, Color accent)
        {
            button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            button.AddThemeStyleboxOverride("normal", CreateRaisedButtonStyle(
                accent.Darkened(0.68f),
                accent.Darkened(0.08f),
                8,
                7));
            button.AddThemeStyleboxOverride("hover", CreateRaisedButtonStyle(
                accent.Darkened(0.48f),
                accent.Lightened(0.04f),
                8,
                8));
            button.AddThemeStyleboxOverride("pressed", CreateRaisedButtonStyle(
                accent.Darkened(0.78f),
                accent,
                8,
                11));
            button.AddThemeStyleboxOverride("disabled", CreateRaisedButtonStyle(
                new Color(0.07f, 0.075f, 0.075f, 0.78f),
                WithAlpha(MutedText, 0.35f),
                8,
                7));
            button.AddThemeColorOverride("font_color", Colors.White.Lightened(0.04f));
            button.AddThemeColorOverride("font_hover_color", Colors.White);
            button.AddThemeColorOverride("font_pressed_color", Colors.White);
        }

        public static void ApplyListRow(PanelContainer panel, bool selected)
        {
            panel.AddThemeStyleboxOverride("panel", GetSharedListRowStyle(selected));
        }

        public static void ApplyTintedListRow(PanelContainer panel, bool selected, Color borderColor)
        {
            StyleBoxFlat style = GetListRowStyle(selected);
            if (!selected)
            {
                style.BorderColor = borderColor;
            }
            panel.AddThemeStyleboxOverride("panel", style);
        }

        public static void ApplyEventPanel(PanelContainer panel, OnlyWarEventTone tone)
        {
            string styleName = tone switch
            {
                OnlyWarEventTone.Critical => "critical",
                OnlyWarEventTone.Warning => "warning",
                _ => "normal"
            };
            panel.AddThemeStyleboxOverride("panel", GetStylebox(styleName, EventPanelType));
        }

        public static void ApplyAccentButtonRow(Button button, bool selected, Color accent)
        {
            button.AddThemeStyleboxOverride("normal", CreateAccentButtonStyle(selected, accent, false));
            button.AddThemeStyleboxOverride("hover", CreateAccentButtonStyle(true, accent, true));
            button.AddThemeStyleboxOverride("pressed", CreateAccentButtonStyle(true, accent, true));
            // Keep the disabled state on the same geometry as the action-row states. Falling back
            // to the theme's ButtonDisabled style uses different vertical content margins, which
            // changes the button minimum height and can move/resize its containing panel.
            button.AddThemeStyleboxOverride("disabled", CreateDisabledAccentButtonStyle());
        }

        public static void ApplyDialogCloseButton(Button button)
        {
            button.FocusMode = Control.FocusModeEnum.None;
            button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            button.AddThemeStyleboxOverride("normal", CreateCloseButtonStyle(
                new Color(0.015f, 0.018f, 0.019f, 0.94f),
                WithAlpha(Gold, 0.58f)));
            button.AddThemeStyleboxOverride("hover", CreateCloseButtonStyle(
                new Color(0.20f, 0.045f, 0.035f, 0.98f),
                WithAlpha(Critical, 0.94f)));
            button.AddThemeStyleboxOverride("pressed", CreateCloseButtonStyle(
                new Color(0.29f, 0.055f, 0.04f, 1.0f),
                Critical));
            button.AddThemeStyleboxOverride("disabled", CreateCloseButtonStyle(
                new Color(0.015f, 0.018f, 0.019f, 0.45f),
                WithAlpha(MutedText, 0.25f)));
            button.AddThemeColorOverride("icon_normal_color", BodyText);
            button.AddThemeColorOverride("icon_hover_color", new Color(1.0f, 0.82f, 0.72f));
            button.AddThemeColorOverride("icon_pressed_color", new Color(1.0f, 0.92f, 0.82f));
        }

        public static void ApplyListRow(Button button, bool selected, bool enabled = true)
        {
            // The enabled path is the list-rendering hot path: four stylebox copies per row added
            // up to hundreds of Resource allocations on a single Muster rebuild. Nothing mutates
            // them here, so hand out the shared instances and only copy for the dimmed variant.
            if (enabled)
            {
                StyleBoxFlat shared = GetSharedListRowStyle(selected);
                StyleBoxFlat sharedSelected = GetSharedListRowStyle(true);
                button.AddThemeStyleboxOverride("normal", shared);
                button.AddThemeStyleboxOverride("disabled", shared);
                button.AddThemeStyleboxOverride("hover", sharedSelected);
                button.AddThemeStyleboxOverride("pressed", sharedSelected);
                return;
            }

            StyleBoxFlat normal = GetListRowStyle(selected);
            normal.BgColor = WithAlpha(normal.BgColor, 0.48f);
            normal.BorderColor = WithAlpha(normal.BorderColor, 0.42f);
            button.AddThemeStyleboxOverride("normal", normal);
            button.AddThemeStyleboxOverride("disabled", (StyleBoxFlat)normal.Duplicate());
            button.AddThemeStyleboxOverride("hover", GetSharedListRowStyle(true));
            button.AddThemeStyleboxOverride("pressed", GetSharedListRowStyle(true));
        }

        /// <summary>
        /// A private, mutable copy of the list-row stylebox. Use this when the caller needs to tweak
        /// margins or colors (see <c>RosterRowStyle.ApplyCompactSoldierRow</c>); use
        /// <see cref="GetSharedListRowStyle"/> when the style is applied unmodified.
        /// </summary>
        public static StyleBoxFlat GetListRowStyle(bool selected)
        {
            return GetStylebox(selected ? "selected" : "normal", ListRowType);
        }

        /// <summary>
        /// The shared list-row stylebox instance. Never mutate the result - it is referenced by
        /// every row in every screen that renders one.
        /// </summary>
        public static StyleBoxFlat GetSharedListRowStyle(bool selected)
        {
            return GetSharedStylebox(selected ? "selected" : "normal", ListRowType);
        }

        public static StyleBoxFlat GetInsetPanelStyle()
        {
            return GetStylebox("panel", InsetPanelType);
        }

        private static StyleBoxFlat CreateAccentButtonStyle(bool selected, Color accent, bool hover)
        {
            StyleBoxFlat style = GetListRowStyle(false);
            style.BgColor = selected
                ? new Color(accent.R, accent.G, accent.B, hover ? 0.24f : 0.18f)
                : new Color(0.01f, 0.012f, 0.014f, hover ? 0.96f : 0.72f);
            style.BorderColor = selected ? accent : new Color(0.33f, 0.28f, 0.18f, 0.67f);
            style.ContentMarginLeft = 8;
            style.ContentMarginTop = 5;
            style.ContentMarginRight = 8;
            style.ContentMarginBottom = 5;
            return style;
        }

        private static StyleBoxFlat CreateDisabledAccentButtonStyle()
        {
            StyleBoxFlat style = CreateAccentButtonStyle(false, Gold, false);
            style.BgColor = new Color(0.025f, 0.028f, 0.029f, 0.68f);
            style.BorderColor = WithAlpha(MutedText, 0.30f);
            return style;
        }

        private static StyleBoxFlat CreateCloseButtonStyle(Color background, Color border)
        {
            return new StyleBoxFlat
            {
                BgColor = background,
                BorderColor = border,
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 2,
                CornerRadiusTopRight = 2,
                CornerRadiusBottomLeft = 2,
                CornerRadiusBottomRight = 2,
                ContentMarginLeft = 4,
                ContentMarginTop = 4,
                ContentMarginRight = 4,
                ContentMarginBottom = 4
            };
        }

        private static StyleBoxFlat GetDataPanelStyle()
        {
            return new StyleBoxFlat
            {
                BgColor = new Color(0.010f, 0.014f, 0.016f, 0.94f),
                BorderColor = new Color(0.33f, 0.28f, 0.18f, 0.60f),
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 2,
                CornerRadiusTopRight = 2,
                CornerRadiusBottomLeft = 2,
                CornerRadiusBottomRight = 2,
                ContentMarginLeft = 14,
                ContentMarginTop = 12,
                ContentMarginRight = 14,
                ContentMarginBottom = 12
            };
        }

        private static StyleBoxFlat CreateActionButtonStyle(Color background, Color border, int topMargin)
        {
            return new StyleBoxFlat
            {
                BgColor = background,
                BorderColor = border,
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 2,
                CornerRadiusTopRight = 2,
                CornerRadiusBottomLeft = 2,
                CornerRadiusBottomRight = 2,
                ContentMarginLeft = 14,
                ContentMarginTop = topMargin,
                ContentMarginRight = 14,
                ContentMarginBottom = 10,
                ShadowColor = new Color(0.0f, 0.0f, 0.0f, 0.28f),
                ShadowSize = 3
            };
        }

        private static StyleBoxFlat CreateRaisedButtonStyle(Color background, Color border, int radius, int shadowSize)
        {
            return new StyleBoxFlat
            {
                BgColor = WithAlpha(background, 0.98f),
                BorderColor = WithAlpha(border, 0.98f),
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 2,
                CornerRadiusTopLeft = radius,
                CornerRadiusTopRight = radius,
                CornerRadiusBottomLeft = radius,
                CornerRadiusBottomRight = radius,
                ContentMarginLeft = 16,
                ContentMarginTop = 9,
                ContentMarginRight = 16,
                ContentMarginBottom = 9,
                ShadowColor = new Color(0.0f, 0.0f, 0.0f, 0.62f),
                ShadowSize = shadowSize
            };
        }

        private static readonly System.Collections.Generic.Dictionary<string, StyleBoxFlat> SharedStyleboxes = [];

        // Cached, shared instance of a theme stylebox. Only for callers that apply it unmodified.
        private static StyleBoxFlat GetSharedStylebox(string styleName, string themeType)
        {
            string key = themeType + "/" + styleName;
            if (SharedStyleboxes.TryGetValue(key, out StyleBoxFlat cached))
            {
                return cached;
            }

            StyleBoxFlat style = GetStylebox(styleName, themeType);
            SharedStyleboxes[key] = style;
            return style;
        }

        private static StyleBoxFlat GetStylebox(string styleName, string themeType)
        {
            Theme theme = GetTheme();
            if (theme != null && theme.HasStylebox(styleName, themeType))
            {
                return (StyleBoxFlat)theme.GetStylebox(styleName, themeType).Duplicate();
            }

            GD.PushWarning($"Missing OnlyWar theme stylebox: {themeType}/{styleName}");
            return new StyleBoxFlat
            {
                BgColor = new Color(0.02f, 0.023f, 0.024f, 0.92f),
                BorderColor = new Color(0.33f, 0.28f, 0.18f, 0.72f),
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 2,
                CornerRadiusTopRight = 2,
                CornerRadiusBottomLeft = 2,
                CornerRadiusBottomRight = 2,
                ContentMarginLeft = 10,
                ContentMarginTop = 8,
                ContentMarginRight = 10,
                ContentMarginBottom = 8
            };
        }

        private static Theme GetTheme()
        {
            _theme ??= GD.Load<Theme>(ThemePath);
            return _theme;
        }
    }
}
