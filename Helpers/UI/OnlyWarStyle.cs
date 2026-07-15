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
        public static readonly Color MedicalStable = new(0.34f, 0.64f, 0.37f);
        public static readonly Color MedicalWarning = new(0.83f, 0.63f, 0.31f);
        public static readonly Color Critical = new(0.92f, 0.28f, 0.20f);

        public static Color WithAlpha(Color color, float alpha)
        {
            color.A = alpha;
            return color;
        }

        public static void ApplyContentPanel(PanelContainer panel)
        {
            panel.AddThemeStyleboxOverride("panel", GetStylebox("panel", ContentPanelType));
        }

        public static void ApplyContentPanel(Panel panel)
        {
            panel.AddThemeStyleboxOverride("panel", GetStylebox("panel", ContentPanelType));
        }

        public static void ApplyInsetPanel(PanelContainer panel)
        {
            panel.AddThemeStyleboxOverride("panel", GetStylebox("panel", InsetPanelType));
        }

        public static void ApplyInsetPanel(Panel panel)
        {
            panel.AddThemeStyleboxOverride("panel", GetStylebox("panel", InsetPanelType));
        }

        public static void ApplyListRow(PanelContainer panel, bool selected)
        {
            panel.AddThemeStyleboxOverride("panel", GetListRowStyle(selected));
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
        }

        public static void ApplyListRow(Button button, bool selected, bool enabled = true)
        {
            StyleBoxFlat normal = GetListRowStyle(selected);
            if (!enabled)
            {
                normal.BgColor = WithAlpha(normal.BgColor, 0.48f);
                normal.BorderColor = WithAlpha(normal.BorderColor, 0.42f);
            }
            button.AddThemeStyleboxOverride("normal", normal);
            button.AddThemeStyleboxOverride("disabled", (StyleBoxFlat)normal.Duplicate());
            button.AddThemeStyleboxOverride("hover", GetListRowStyle(true));
            button.AddThemeStyleboxOverride("pressed", GetListRowStyle(true));
        }

        public static StyleBoxFlat GetListRowStyle(bool selected)
        {
            return GetStylebox(selected ? "selected" : "normal", ListRowType);
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
