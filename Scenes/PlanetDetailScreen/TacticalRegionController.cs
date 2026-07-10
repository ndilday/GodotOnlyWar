using Godot;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System;
using System.Linq;

public partial class TacticalRegionController : Control
{
    private static readonly Color ContestedRegionColor = new(0.92f, 0.43f, 0.10f);

    private TacticalRegionView _view;
    private Button _button;
    private Region _region;

    public event EventHandler<Region> TacticalRegionPressed;

    public override void _Ready()
    {
        _view = GetNode<TacticalRegionView>("TacticalRegionView");
        _button = GetNode<Button>("TacticalRegionView/Button");
        _button.Pressed += () => TacticalRegionPressed?.Invoke(this, _region);
    }

    public void Populate(Region region, MapLayer layers = MapLayer.None, bool selected = false)
    {
        _region = region;
        RegionFaction playerRegionFaction = region.RegionFactionMap.Values.FirstOrDefault(rf => rf.PlanetFaction.Faction.IsPlayerFaction);
        RegionFaction defaultFaction = region.RegionFactionMap.Values.FirstOrDefault(rf => rf.PlanetFaction.Faction.IsDefaultFaction);
        // Prefer a public enemy over a still-hidden one (a Tyranid incursion can sit on top of a
        // hidden Genestealer Cult); otherwise the hex would surface the hidden faction and render
        // its headcount under the Imperial-civilian icon.
        RegionFaction xenosRegionFaction = region.GetVisibleEnemyRegionFaction();

        int playerCount = playerRegionFaction?.LandedSquads.Sum(s => s.Members.Count()) ?? 0;
        int assignedCount = playerRegionFaction?.LandedSquads.Count(s => s.CurrentOrders != null) ?? 0;
        int unassignedCount = playerRegionFaction?.LandedSquads.Count(s => s.CurrentOrders == null) ?? 0;
        bool hiddenImperialPopulation = region.HasHiddenDefaultFaction();
        long civilianPopulation = hiddenImperialPopulation ? 0 : region.GetVisibleCivilianPopulation();
        long garrison = region.PlanetaryDefenseForces;
        bool publicEnemy = xenosRegionFaction != null && xenosRegionFaction.IsPublic;
        bool hiddenEnemy = xenosRegionFaction != null && !xenosRegionFaction.IsPublic;
        float visibleIntel = region.GetPlayerVisibleIntel();

        bool showForces = layers.HasFlag(MapLayer.Forces);
        bool showOrders = layers.HasFlag(MapLayer.Orders);
        bool showIntel = layers.HasFlag(MapLayer.Intel);
        bool showEntrenchment = showIntel && publicEnemy && visibleIntel > 1;

        // Layers combine rather than exclude: a tile can show force strength, order
        // status, and intel simultaneously if all three layers are toggled on.
        bool showPlayerPublic = (showForces || showOrders) && playerCount > 0;
        string playerPopulation = showOrders && playerRegionFaction != null
            ? $"{assignedCount}/{playerRegionFaction.LandedSquads.Count}"
            : (playerCount > 0 ? playerCount.ToString() : "");

        bool showXenos = (showForces || showIntel) && publicEnemy;
        string xenosText = showXenos ? xenosRegionFaction.GetPopulationDescription() : "";

        bool showPlayerHidden = (showForces && hiddenEnemy && visibleIntel > 0)
            || (showIntel && hiddenEnemy)
            || (showOrders && unassignedCount > 0);

        bool showCivilian = showEntrenchment || garrison > 0 || (showForces && (civilianPopulation > 0 || hiddenImperialPopulation));
        string civilianText = showEntrenchment
            ? RegionFactionExtensions.GetDefenseLevelDescription(xenosRegionFaction.Entrenchment)
            : garrison > 0 ? FormatCompact(garrison) : (hiddenImperialPopulation ? "?" : (showForces ? FormatCompact(civilianPopulation) : ""));

        bool showObjective = region.SpecialMissions.Count > 0 || (showOrders && assignedCount > 0);
        const bool showDropPod = false;
        string xenosIconKey = IconAtlas.GetFactionIconKey(xenosRegionFaction?.PlanetFaction.Faction);
        string civilianIconKey = showEntrenchment
            ? xenosIconKey
            : garrison > 0 ? "pdf_forces" : "imperial_population";
        bool hiddenEnemyMarker = (showForces && hiddenEnemy && visibleIntel > 0)
            || (showIntel && hiddenEnemy);
        string hiddenIconKey = hiddenEnemyMarker ? xenosIconKey : "player_forces";

        Color color;
        if (region.ControllingFaction == null)
        {
            color = MutedMapColor(ContestedRegionColor, 0.10f);
        }
        else if (showOrders && assignedCount > 0)
        {
            color = new Color(0.46f, 0.36f, 0.16f);
        }
        else if (playerCount > 0 && (showForces || showOrders))
        {
            color = MutedMapColor(playerRegionFaction.PlanetFaction.Faction.Color.ToGodotColor(), 0.18f);
        }
        else if (publicEnemy && (showForces || showIntel))
        {
            color = MutedMapColor(xenosRegionFaction.PlanetFaction.Faction.Color.ToGodotColor(), 0.18f);
        }
        else
        {
            color = MutedMapColor(GetControlColor(region), 0.34f);
        }

        string civilianTooltip = showEntrenchment
            ? $"Enemy Entrenchment: {RegionFactionExtensions.GetDefenseLevelDescription(xenosRegionFaction.Entrenchment)}"
            : garrison > 0 ? $"PDF Garrison: {garrison:N0}"
            : hiddenImperialPopulation ? "Imperial Population: Unknown"
            : $"Imperial Population: {civilianPopulation:N0}";
        string playerTooltip = showOrders && playerRegionFaction != null
            ? $"Space Marines: {playerCount} ({assignedCount}/{playerRegionFaction.LandedSquads.Count} squads assigned)"
            : $"Space Marines: {playerCount}";
        string xenosTooltip = showXenos
            ? $"{xenosRegionFaction.PlanetFaction.Faction.Name}: {xenosText}"
            : "";

        _view.Populate(
            region.Id,
            region.Name,
            showPlayerPublic,
            showPlayerHidden,
            showCivilian,
            showXenos,
            showObjective,
            showDropPod,
            civilianIconKey,
            "player_forces",
            hiddenIconKey,
            xenosIconKey,
            playerPopulation,
            civilianText,
            xenosText,
            civilianTooltip,
            playerTooltip,
            xenosTooltip,
            color,
            selected);
    }

    public void AddToButtonGroup(ButtonGroup buttonGroup)
    {
        _button.ButtonGroup = buttonGroup;
    }

    private static Color GetControlColor(Region region)
    {
        return region.ControllingFaction != null
            ? region.ControllingFaction.PlanetFaction.Faction.Color.ToGodotColor()
            : ContestedRegionColor;
    }

    private static string FormatCompact(long value)
    {
        if (value >= 1_000_000_000) return $"{value / 1_000_000_000.0:0.#}B";
        if (value >= 1_000_000) return $"{value / 1_000_000.0:0.#}M";
        if (value >= 1_000) return $"{value / 1_000.0:0.#}K";
        return value.ToString();
    }

    private static Color MutedMapColor(Color source, float neutralMix)
    {
        Color toned = source.Darkened(0.42f);
        Color neutral = new(0.08f, 0.10f, 0.10f);
        toned = toned.Lerp(neutral, neutralMix);
        toned.A = 1f;
        return toned;
    }
}
