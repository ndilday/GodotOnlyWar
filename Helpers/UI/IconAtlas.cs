using Godot;
using OnlyWar.Models;
using System.Collections.Generic;

namespace OnlyWar.Helpers.UI
{
    public static class IconAtlas
    {
        private const string AtlasPath = "res://Assets/UI/Icons/icon_atlas.png";
        private const int CellSize = 128;
        private static Texture2D _atlasTexture;

        private static readonly Dictionary<string, Vector2I> IconCells = new()
        {
            ["sector"] = new Vector2I(0, 0),
            ["chapter"] = new Vector2I(1, 0),
            ["apothecarium"] = new Vector2I(2, 0),
            ["reclusium"] = new Vector2I(3, 0),
            ["librarium"] = new Vector2I(4, 0),
            ["armamentarium"] = new Vector2I(5, 0),
            ["training_unit"] = new Vector2I(6, 0),
            ["fleet"] = new Vector2I(7, 0),
            ["diplomacy"] = new Vector2I(0, 1),
            ["archive"] = new Vector2I(1, 1),
            ["end_turn"] = new Vector2I(2, 1),
            ["save"] = new Vector2I(3, 1),
            ["settings"] = new Vector2I(4, 1),
            ["menu"] = new Vector2I(5, 1),
            ["close"] = new Vector2I(6, 1),
            ["alert"] = new Vector2I(7, 1),
            ["zoom_in"] = new Vector2I(0, 2),
            ["zoom_out"] = new Vector2I(1, 2),
            ["focus"] = new Vector2I(2, 2),
            ["layers"] = new Vector2I(3, 2),
            ["filter"] = new Vector2I(4, 2),
            ["route"] = new Vector2I(5, 2),
            ["warp_lane"] = new Vector2I(6, 2),
            ["map_pin"] = new Vector2I(7, 2),
            ["star"] = new Vector2I(0, 3),
            ["planet"] = new Vector2I(1, 3),
            ["controlled"] = new Vector2I(2, 3),
            ["allied"] = new Vector2I(3, 3),
            ["neutral"] = new Vector2I(4, 3),
            ["hostile"] = new Vector2I(5, 3),
            ["request"] = new Vector2I(6, 3),
            ["threat"] = new Vector2I(7, 3),
            ["resource"] = new Vector2I(0, 4),
            ["population"] = new Vector2I(0, 6),
            ["fleet_strength"] = new Vector2I(1, 6),
            ["construction"] = new Vector2I(1, 4),
            ["plot_course"] = new Vector2I(2, 4),
            ["divide"] = new Vector2I(3, 4),
            ["merge"] = new Vector2I(4, 4),
            ["land_squads"] = new Vector2I(5, 4),
            ["load_squads"] = new Vector2I(6, 4),
            ["in_orbit"] = new Vector2I(7, 4),
            ["hq"] = new Vector2I(1, 0),
            ["scout"] = new Vector2I(1, 5),
            ["elite"] = new Vector2I(2, 5),
            ["default"] = new Vector2I(3, 5),
            ["fast"] = new Vector2I(4, 5),
            ["heavy"] = new Vector2I(5, 5),
            ["tactical"] = new Vector2I(3, 5),
            ["assault"] = new Vector2I(4, 5),
            ["devastator"] = new Vector2I(5, 5),
            ["bodyguard"] = new Vector2I(6, 5),
            ["vehicle"] = new Vector2I(7, 5),
            ["infantry"] = new Vector2I(0, 6),
            ["ship"] = new Vector2I(1, 6),
            ["objective"] = new Vector2I(2, 6),
            ["wounded"] = new Vector2I(3, 6),
            ["medical"] = new Vector2I(4, 6),
            ["training"] = new Vector2I(5, 6),
            ["locked"] = new Vector2I(6, 6),
            ["in_transit"] = new Vector2I(7, 6),
            ["rank_initiate"] = new Vector2I(0, 7),
            ["rank_battle_brother"] = new Vector2I(1, 7),
            ["rank_veteran"] = new Vector2I(2, 7),
            ["rank_sergeant"] = new Vector2I(3, 7),
            ["rank_captain"] = new Vector2I(4, 7),
            ["rank_commander"] = new Vector2I(5, 7),
            ["award"] = new Vector2I(6, 7),
            ["in_warp"] = new Vector2I(7, 7),
            ["imperial_population"] = new Vector2I(0, 8),
            ["pdf_forces"] = new Vector2I(1, 8),
            ["player_forces"] = new Vector2I(2, 8),
            ["faction_tyranids"] = new Vector2I(3, 8),
            ["faction_genestealer_cult"] = new Vector2I(4, 8),
            ["faction_chaos"] = new Vector2I(5, 8)
        };

        public static string GetFactionIconKey(Faction faction)
        {
            if (faction == null) return "hostile";
            if (faction.IsPlayerFaction) return "player_forces";
            if (faction.IsDefaultFaction) return "pdf_forces";

            string name = faction.Name?.ToLowerInvariant() ?? "";
            if (name.Contains("tyranid")) return "faction_tyranids";
            if (name.Contains("genestealer")) return "faction_genestealer_cult";
            if (name.Contains("chaos")) return "faction_chaos";
            return "hostile";
        }

        public static AtlasTexture GetIcon(string key)
        {
            if (!IconCells.TryGetValue(key, out Vector2I cell))
            {
                GD.PushWarning($"Unknown icon atlas key: {key}");
                return null;
            }

            _atlasTexture ??= GD.Load<Texture2D>(AtlasPath);
            if (_atlasTexture == null)
            {
                GD.PushWarning($"Icon atlas failed to load: {AtlasPath}");
                return null;
            }

            return new AtlasTexture
            {
                Atlas = _atlasTexture,
                Region = new Rect2(cell.X * CellSize, cell.Y * CellSize, CellSize, CellSize)
            };
        }

        public static void Apply(Button button, string key, int minWidth = 0)
        {
            button.Icon = GetIcon(key);
            button.IconAlignment = HorizontalAlignment.Left;
            button.ExpandIcon = false;
            button.AddThemeConstantOverride("icon_max_width", 48);
            button.AddThemeConstantOverride("h_separation", 6);
            if (minWidth > 0)
            {
                Vector2 minimumSize = button.CustomMinimumSize;
                minimumSize.X = minWidth;
                button.CustomMinimumSize = minimumSize;
            }
        }

        public static void ApplyIconButton(Button button, string key, int size = 36, int iconMaxWidth = 28)
        {
            button.Text = "";
            button.Icon = GetIcon(key);
            button.IconAlignment = HorizontalAlignment.Center;
            button.ExpandIcon = false;
            button.CustomMinimumSize = new Vector2(size, size);
            button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            button.AddThemeConstantOverride("icon_max_width", iconMaxWidth);
            button.AddThemeConstantOverride("h_separation", 0);
        }
    }
}
