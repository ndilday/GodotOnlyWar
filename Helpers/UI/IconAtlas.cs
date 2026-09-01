using Godot;
using OnlyWar.Models;
using OnlyWar.Models.Squads;
using System.Collections.Generic;

namespace OnlyWar.Helpers.UI
{
	public static class IconAtlas
	{
		private const string AtlasPath = "res://Assets/UI/Icons/icon_atlas.png";
		private const int CellSize = 128;
		private const string PlanetaryOperationsFactionAtlasPath =
			"res://Assets/UI/Icons/map_icon_atlas.png";
		private const int PlanetaryOperationsFactionCellSize = 32;
		private static Texture2D _atlasTexture;
		private static Texture2D _planetaryOperationsFactionAtlasTexture;

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
			["faction_chaos"] = new Vector2I(5, 8),
			["honor_gun"] = new Vector2I(0, 9),
			["honor_sword"] = new Vector2I(1, 9),
			["honor_voice"] = new Vector2I(2, 9),
			["honor_banner"] = new Vector2I(3, 9),
			["squad_lineage"] = new Vector2I(4, 9),
			["formation_create"] = new Vector2I(5, 9),
			["fleet_rebalance"] = new Vector2I(6, 9),
			["sort"] = new Vector2I(0, 10),
			["recovery_time"] = new Vector2I(1, 10),
			["limb_replacement"] = new Vector2I(2, 10),
			["medical_detachment"] = new Vector2I(3, 10),
			["individual_posting"] = new Vector2I(4, 10),
			["reunion"] = new Vector2I(5, 10),
			["control_contested"] = new Vector2I(0, 11),
			["mission_recon"] = new Vector2I(1, 11),
			["mission_defend"] = new Vector2I(2, 11),
			["mission_patrol"] = new Vector2I(3, 11),
			["mission_attack"] = new Vector2I(4, 11),
			["mission_diversion"] = new Vector2I(5, 11),
			["fortification_entrenchment"] = new Vector2I(6, 11),
			["fortification_listening_post"] = new Vector2I(7, 11),
			["fortification_anti_air"] = new Vector2I(0, 12),
			["mission_ambush"] = new Vector2I(1, 12),
			["mission_sabotage"] = new Vector2I(2, 12),
			["mission_show_of_force"] = new Vector2I(3, 12),
			["order_active"] = new Vector2I(4, 12),
			["order_assigned"] = new Vector2I(5, 12),
			["order_unassigned"] = new Vector2I(6, 12)
		};

		private static readonly Dictionary<string, Rect2> PlanetaryOperationsFactionRegions = new()
		{
			["map_imperial"] = new Rect2(0, 0, 64, PlanetaryOperationsFactionCellSize),
			["map_player"] = new Rect2(0, PlanetaryOperationsFactionCellSize,
				PlanetaryOperationsFactionCellSize, PlanetaryOperationsFactionCellSize),
			["map_tyranids"] = new Rect2(PlanetaryOperationsFactionCellSize,
				PlanetaryOperationsFactionCellSize, PlanetaryOperationsFactionCellSize,
				PlanetaryOperationsFactionCellSize),
			["map_genestealer_cult"] = new Rect2(PlanetaryOperationsFactionCellSize * 2,
				PlanetaryOperationsFactionCellSize, PlanetaryOperationsFactionCellSize,
				PlanetaryOperationsFactionCellSize),
			["map_orks"] = new Rect2(PlanetaryOperationsFactionCellSize * 3,
				PlanetaryOperationsFactionCellSize, PlanetaryOperationsFactionCellSize,
				PlanetaryOperationsFactionCellSize)
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

		public static string GetPlanetaryOperationsFactionIconKey(Faction faction)
		{
			if (faction == null) return null;
			if (faction.IsPlayerFaction) return "map_player";
			if (faction.IsDefaultFaction) return "map_imperial";

			string name = faction.Name?.ToLowerInvariant() ?? "";
			if (name.Contains("tyranid")) return "map_tyranids";
			if (name.Contains("genestealer")) return "map_genestealer_cult";
			if (name.Contains("ork")) return "map_orks";
			// Faction art is required content. Returning no key makes the missing-art path loud in
			// RegionMapCardView instead of falsely representing a new faction as Orks.
			return null;
		}

		public static string GetSquadIconKey(SquadTemplate template)
		{
			if (template == null) return "infantry";

			SquadTypes type = template.SquadType;
			if (type.HasFlag(SquadTypes.HQ)) return "hq";
			if (type.HasFlag(SquadTypes.Elite)) return "elite";
			if (type.HasFlag(SquadTypes.Bodyguard)) return "bodyguard";
			if (type.HasFlag(SquadTypes.Heavy)) return "devastator";
			if (type.HasFlag(SquadTypes.Fast)) return "assault";
			if (type.HasFlag(SquadTypes.Scout)) return "scout";
			return "tactical";
		}

		// One AtlasTexture per key, shared by every node that draws it. A list page previously
		// allocated one Resource per icon per row (~280 for a full Muster candidate page), which
		// cost both the Godot-side object and its C# wrapper on every rebuild. Callers only ever
		// assign the result to Texture/Icon/SetIcon; none mutate Atlas or Region, so sharing is safe.
		private static readonly Dictionary<string, AtlasTexture> IconTextures = [];
		private static readonly Dictionary<string, AtlasTexture> PlanetaryOperationsFactionTextures = [];

		public static Texture2D GetIcon(string key)
		{
			Texture2D registered = IconAssetRegistry.Resolve(key);
			if (registered != null)
			{
				return registered;
			}

			if (key != null && IconTextures.TryGetValue(key, out AtlasTexture cached))
			{
				return cached;
			}

			string builtInKey = key?.StartsWith("core:", System.StringComparison.OrdinalIgnoreCase) == true
				? key[5..]
				: key;
			if (!IconCells.TryGetValue(builtInKey, out Vector2I cell))
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

			AtlasTexture texture = new()
			{
				Atlas = _atlasTexture,
				Region = new Rect2(cell.X * CellSize, cell.Y * CellSize, CellSize, CellSize)
			};
			IconTextures[builtInKey] = texture;
			return texture;
		}

		public static bool HasIcon(string key) => IconAssetRegistry.HasIcon(key)
			|| IconCells.ContainsKey(key?.StartsWith("core:", System.StringComparison.OrdinalIgnoreCase) == true
				? key[5..]
				: key);

		public static void RegisterModIconManifest(string manifestPath, string modId) =>
			IconAssetRegistry.RegisterManifest(manifestPath, modId);

		public static void ClearRegisteredModIcons() => IconAssetRegistry.ClearRegisteredMods();

		public static void ClearModIconManifest(string modId) =>
			IconAssetRegistry.ClearPackage(modId);

		public static bool HasPlanetaryOperationsFactionIcon(string key) =>
			PlanetaryOperationsFactionRegions.ContainsKey(key);

		public static AtlasTexture GetPlanetaryOperationsFactionIcon(string key)
		{
			if (key != null && PlanetaryOperationsFactionTextures.TryGetValue(key,
				out AtlasTexture cached))
			{
				return cached;
			}

			if (!PlanetaryOperationsFactionRegions.TryGetValue(key, out Rect2 region))
			{
				GD.PushWarning($"Unknown planetary operations faction icon key: {key}");
				return null;
			}

			_planetaryOperationsFactionAtlasTexture ??=
				GD.Load<Texture2D>(PlanetaryOperationsFactionAtlasPath);
			if (_planetaryOperationsFactionAtlasTexture == null)
			{
				GD.PushWarning(
					$"Planetary operations faction atlas failed to load: "
					+ PlanetaryOperationsFactionAtlasPath);
				return null;
			}

			AtlasTexture texture = new()
			{
				Atlas = _planetaryOperationsFactionAtlasTexture,
				Region = region
			};
			PlanetaryOperationsFactionTextures[key] = texture;
			return texture;
		}

		public static void Apply(Button button, string key, int minWidth = 0)
		{
			button.Icon = GetIcon(key);
			button.IconAlignment = HorizontalAlignment.Left;
			button.ExpandIcon = false;
			button.Set("fixed_icon_size", Vector2I.Zero);
			button.AddThemeConstantOverride("icon_max_width", 32);
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
			button.Set("fixed_icon_size", Vector2I.Zero);
			button.CustomMinimumSize = new Vector2(size, size);
			button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
			button.AddThemeConstantOverride("icon_max_width", iconMaxWidth);
			button.AddThemeConstantOverride("h_separation", 0);
		}
	}
}
