using Godot;
using OnlyWar.Helpers.Extensions;
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

    public void Populate(Region region, PlanetCommandMode mode = PlanetCommandMode.Overview, bool selected = false)
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

        bool showPlayerPublic = false;
        bool showPlayerHidden = false;
        bool showCivilian = false;
        bool showXenos = false;
        bool showObjective = false;
        bool showDropPod = false;
        string playerPopulation = "";
        string civilianText = "";
        string xenosText = "";
        Color color = MutedMapColor(GetControlColor(region), 0.34f);

        switch (mode)
        {
            case PlanetCommandMode.Forces:
                showPlayerPublic = playerCount > 0;
                playerPopulation = playerCount > 0 ? playerCount.ToString() : "";
                showCivilian = garrison > 0 || civilianPopulation > 0;
                civilianText = garrison > 0 ? FormatCompact(garrison) : FormatCompact(civilianPopulation);
                showXenos = publicEnemy;
                xenosText = publicEnemy ? xenosRegionFaction.GetPopulationDescription() : "";
                showPlayerHidden = hiddenEnemy && region.IntelligenceLevel > 0;
                color = playerCount > 0
                    ? MutedMapColor(playerRegionFaction.PlanetFaction.Faction.Color.ToGodotColor(), 0.18f)
                    : publicEnemy ? MutedMapColor(xenosRegionFaction.PlanetFaction.Faction.Color.ToGodotColor(), 0.18f) : MutedMapColor(GetControlColor(region), 0.42f);
                break;
            case PlanetCommandMode.Orders:
                showPlayerPublic = playerCount > 0;
                playerPopulation = playerCount > 0 ? $"{assignedCount}/{playerRegionFaction.LandedSquads.Count}" : "";
                showObjective = assignedCount > 0;
                showPlayerHidden = unassignedCount > 0;
                color = assignedCount > 0 ? new Color(0.46f, 0.36f, 0.16f) : MutedMapColor(GetControlColor(region), 0.48f);
                break;
            case PlanetCommandMode.Logistics:
                showPlayerPublic = playerCount > 0;
                playerPopulation = playerCount > 0 ? playerCount.ToString() : "";
                showDropPod = true;
                showCivilian = garrison > 0;
                civilianText = garrison > 0 ? FormatCompact(garrison) : "";
                color = selected ? new Color(0.10f, 0.42f, 0.47f) : MutedMapColor(GetControlColor(region), 0.46f);
                break;
            case PlanetCommandMode.Intel:
                showXenos = publicEnemy;
                xenosText = publicEnemy ? xenosRegionFaction.GetPopulationDescription() : "";
                showPlayerHidden = hiddenEnemy;
                showCivilian = publicEnemy && region.IntelligenceLevel > 1;
                civilianText = publicEnemy && region.IntelligenceLevel > 1
                    ? RegionFactionExtensions.GetDefenseLevelDescription(xenosRegionFaction.Entrenchment)
                    : "";
                showObjective = region.SpecialMissions.Count > 0;
                color = publicEnemy ? MutedMapColor(xenosRegionFaction.PlanetFaction.Faction.Color.ToGodotColor(), 0.16f) : MutedMapColor(GetControlColor(region), 0.56f);
                break;
            default:
                showObjective = region.SpecialMissions.Count > 0;
                break;
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
