using Godot;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System;
using System.Linq;

public partial class TacticalRegionController : Control
{
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
        RegionFaction xenosRegionFaction = region.RegionFactionMap.Values.FirstOrDefault(rf => !rf.PlanetFaction.Faction.IsPlayerFaction && !rf.PlanetFaction.Faction.IsDefaultFaction);

        int playerCount = playerRegionFaction?.LandedSquads.Sum(s => s.Members.Count()) ?? 0;
        int assignedCount = playerRegionFaction?.LandedSquads.Count(s => s.CurrentOrders != null) ?? 0;
        int unassignedCount = playerRegionFaction?.LandedSquads.Count(s => s.CurrentOrders == null) ?? 0;
        long civilianPopulation = GetCivilianPopulation(playerRegionFaction, defaultFaction, xenosRegionFaction);
        long garrison = defaultFaction?.Garrison ?? 0;
        bool publicEnemy = xenosRegionFaction != null && xenosRegionFaction.IsPublic;
        bool hiddenEnemy = xenosRegionFaction != null && !xenosRegionFaction.IsPublic;

        bool showForces = layers.HasFlag(MapLayer.Forces);
        bool showOrders = layers.HasFlag(MapLayer.Orders);
        bool showIntel = layers.HasFlag(MapLayer.Intel);
        bool showEntrenchment = showIntel && publicEnemy && region.IntelligenceLevel > 1;

        // Layers combine rather than exclude: a tile can show force strength, order
        // status, and intel simultaneously if all three layers are toggled on.
        bool showPlayerPublic = (showForces || showOrders) && playerCount > 0;
        string playerPopulation = showOrders && playerRegionFaction != null
            ? $"{assignedCount}/{playerRegionFaction.LandedSquads.Count}"
            : (playerCount > 0 ? playerCount.ToString() : "");

        bool showXenos = (showForces || showIntel) && publicEnemy;
        string xenosText = showXenos ? xenosRegionFaction.GetPopulationDescription() : "";

        bool showPlayerHidden = (showForces && hiddenEnemy && region.IntelligenceLevel > 0)
            || (showIntel && hiddenEnemy)
            || (showOrders && unassignedCount > 0);

        bool showCivilian = showEntrenchment || garrison > 0 || (showForces && civilianPopulation > 0);
        string civilianText = showEntrenchment
            ? RegionFactionExtensions.GetDefenseLevelDescription(xenosRegionFaction.Entrenchment)
            : garrison > 0 ? FormatCompact(garrison) : (showForces ? FormatCompact(civilianPopulation) : "");

        bool showObjective = region.SpecialMissions.Count > 0 || (showOrders && assignedCount > 0);
        const bool showDropPod = false;

        Color color;
        if (showOrders && assignedCount > 0)
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

        _view.Populate(
            region.Id,
            region.Name,
            showPlayerPublic,
            showPlayerHidden,
            showCivilian,
            showXenos,
            showObjective,
            showDropPod,
            playerPopulation,
            civilianText,
            xenosText,
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
            : Colors.DarkRed;
    }

    private static long GetCivilianPopulation(RegionFaction playerRegionFaction, RegionFaction defaultFaction, RegionFaction xenosRegionFaction)
    {
        long population = 0;
        if (defaultFaction != null && defaultFaction.IsPublic)
        {
            population += defaultFaction.Population;
        }
        if (playerRegionFaction != null)
        {
            population += playerRegionFaction.Population;
        }
        if (xenosRegionFaction != null && !xenosRegionFaction.IsPublic)
        {
            population += xenosRegionFaction.Population;
        }
        return population;
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
