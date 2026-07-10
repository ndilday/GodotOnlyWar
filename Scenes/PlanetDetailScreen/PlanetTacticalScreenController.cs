using Godot;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class PlanetTacticalScreenController : DialogController
{
    private const string ActionOpenRegion = "open_region";
    private const string ActionOpenSquad = "open_squad";
    private const string ActionLand = "land";
    private const string ActionLoad = "load";
    private const string ActionEditOrders = "edit_orders";

    private static readonly (MapLayer Layer, string Label, string IconKey)[] MapLayerOptions =
    [
        (MapLayer.Forces, "Forces", "player_forces"),
        (MapLayer.Orders, "Orders", "objective"),
        (MapLayer.Intel, "Intel", "threat")
    ];

    private static readonly (string Key, string Label)[] RosterFilters =
    [
        ("all", "All"),
        ("unassigned", "Unassigned"),
        ("orbit", "In Orbit"),
        ("surface", "Surface"),
        ("injured", "Injured")
    ];

    private PlanetTacticalScreenView _view;
    private TacticalRegionController[] _tacticalRegions;
    private ButtonGroup _buttonGroup;
    private OrderDialogController _orderDialog;

    private Planet _selectedPlanet;
    private MapLayer _activeLayers = MapLayer.Forces | MapLayer.Orders;
    private string _rosterFilter = "all";
    private Region _selectedRegion;
    private Ship _selectedShip;
    private Unit _selectedLoadedUnit;
    private Squad _selectedLoadedSquad;
    private Unit _selectedLandedUnit;
    private Squad _selectedLandedSquad;

    public event EventHandler<Region> RegionDoubleClicked;
    public event EventHandler<Squad> OrbitalSquadDoubleClicked;

    public override void _Ready()
    {
        base._Ready();
        _buttonGroup = new ButtonGroup();
        _view = GetNode<PlanetTacticalScreenView>("PlanetTacticalScreenView");
        _view.SelectionTreeItemSelected += OnSelectionTreeItemSelected;
        _view.SelectionTreeItemActivated += OnSelectionTreeItemActivated;
        _view.CommandPressed += OnCommandPressed;
        _view.MapLayerToggled += OnMapLayerToggled;
        _view.RosterFilterSelected += OnRosterFilterSelected;
        _view.SetMapLayerOptions(MapLayerOptions);
        _view.SetActiveMapLayers(_activeLayers);
        _view.SetRosterFilters(RosterFilters);
        _view.SetActiveRosterFilter(_rosterFilter);

        _orderDialog = GetNode<OrderDialogController>("PlanetTacticalScreenView/OrderDialogController");
        _orderDialog.OrdersConfirmed += OnOrdersConfirmed;

        _tacticalRegions = new TacticalRegionController[16];
        for (int i = 1; i <= 16; i++)
        {
            _tacticalRegions[i - 1] = GetNode<TacticalRegionController>($"PlanetTacticalScreenView/TacticalRegionPanel/TacticalRegionController{i}");
            _tacticalRegions[i - 1].AddToButtonGroup(_buttonGroup);
            _tacticalRegions[i - 1].TacticalRegionPressed += OnTacticalRegionPressed;
        }
    }

    public void PopulatePlanetData(Planet planet)
    {
        _selectedPlanet = planet;
        _selectedRegion = planet?.Regions.FirstOrDefault();
        ClearForceSelections();

        if (planet != null)
        {
            RefreshRegionMap();
        }

        RefreshWorkspace();
    }

    private void OnMapLayerToggled(object sender, MapLayer layer)
    {
        _activeLayers ^= layer;
        _view.SetActiveMapLayers(_activeLayers);
        RefreshRegionMap();
    }

    private void OnRosterFilterSelected(object sender, string key)
    {
        _rosterFilter = key;
        _view.SetActiveRosterFilter(_rosterFilter);
        RefreshWorkspace();
    }

    private void OnTacticalRegionPressed(object sender, Region region)
    {
        _selectedRegion = region;
        _selectedLandedUnit = null;
        _selectedLandedSquad = null;
        RefreshWorkspace();
    }

    private void OnSelectionTreeItemSelected(object sender, string key)
    {
        ApplySelectionKey(key);
        RefreshContextAndCommands();
    }

    private void OnSelectionTreeItemActivated(object sender, string key)
    {
        ApplySelectionKey(key);
        Squad squad = GetSelectedSquad();
        if (squad != null)
        {
            OrbitalSquadDoubleClicked?.Invoke(this, squad);
            return;
        }

        if (_selectedRegion != null)
        {
            RegionDoubleClicked?.Invoke(this, _selectedRegion);
        }
    }

    private void OnCommandPressed(object sender, string key)
    {
        switch (key)
        {
            case ActionOpenRegion:
                if (_selectedRegion != null)
                {
                    RegionDoubleClicked?.Invoke(this, _selectedRegion);
                }
                break;
            case ActionOpenSquad:
                Squad squad = GetSelectedSquad();
                if (squad != null)
                {
                    OrbitalSquadDoubleClicked?.Invoke(this, squad);
                }
                break;
            case ActionEditOrders:
                OpenOrdersDialog();
                break;
            case ActionLand:
                LandSelectedForces();
                break;
            case ActionLoad:
                LoadSelectedForces();
                break;
        }
    }

    private void OpenOrdersDialog()
    {
        if (_selectedLandedSquad == null) return;
        _orderDialog.PopulateOrderData(_selectedLandedSquad);
        _orderDialog.Visible = true;
    }

    private void OnOrdersConfirmed(object sender, EventArgs e)
    {
        RefreshWorkspace();
    }

    public void RefreshFromExternalChange()
    {
        if (_selectedPlanet == null) return;
        RefreshWorkspace();
    }

    private void RefreshWorkspace()
    {
        RefreshRegionMap();
        _view.SetHeader(_selectedPlanet.Name, GetGovernorBadgeText());
        _view.SetSelectionTitle("ROSTER", "Select a region, ship, or squad. Land, load, or open its orders from the command bar.");
        _view.PopulateSelectionTree(BuildRoster());
        RefreshContextAndCommands();
    }

    private string GetGovernorBadgeText()
    {
        Faction controllingFaction = _selectedPlanet.GetControllingFaction();
        if (controllingFaction == null || (!controllingFaction.IsDefaultFaction && !controllingFaction.IsPlayerFaction))
        {
            return null;
        }

        Character governor = _selectedPlanet.PlanetFactionMap[controllingFaction.Id].Leader;
        return governor?.ActiveRequest != null ? "Governor request pending" : null;
    }

    private void RefreshContextAndCommands()
    {
        RefreshRegionMap();
        _view.SetContext(GetContextTitle(), GetContextSubtitle(), BuildContextRows());
        _view.SetCommandRows(BuildCommandRows());
    }

    private IReadOnlyList<CommandTreeNode> BuildRoster()
    {
        if (_selectedPlanet == null) return Array.Empty<CommandTreeNode>();

        List<CommandTreeNode> roots = [BuildRegionsGroup()];
        if (_rosterFilter != "surface")
        {
            roots.Add(new CommandTreeNode("group:orbit", "In Orbit", BuildOrbitShipNodes()));
        }
        if (_rosterFilter != "orbit")
        {
            roots.Add(new CommandTreeNode("group:surface", "Deployed", BuildSurfaceRegionNodes()));
        }
        return roots;
    }

    private CommandTreeNode BuildRegionsGroup()
    {
        List<CommandTreeNode> regionNodes = _selectedPlanet.Regions.Select(region =>
        {
            RegionFaction playerFaction = GetPlayerRegionFaction(region);
            int total = playerFaction?.LandedSquads.Count ?? 0;
            string ordersSuffix = total > 0
                ? $" | {playerFaction.LandedSquads.Count(squad => squad.CurrentOrders != null)}/{total} orders"
                : "";
            return new CommandTreeNode(RegionKey(region.Id), $"{region.Name} | {GetRegionControlLabel(region)}{ordersSuffix}");
        }).ToList();
        return new CommandTreeNode("group:regions", "Regions", regionNodes);
    }

    private IReadOnlyList<CommandTreeNode> BuildOrbitShipNodes()
    {
        List<CommandTreeNode> ships = [];
        Faction playerFaction = GameDataSingleton.Instance.Sector.PlayerForce.Faction;
        foreach (TaskForce taskForce in _selectedPlanet.OrbitingTaskForceList.Where(tf => tf.Faction == playerFaction))
        {
            foreach (Ship ship in taskForce.Ships)
            {
                List<CommandTreeNode> units = [];
                foreach (IGrouping<Unit, Squad> group in ship.LoadedSquads
                    .Where(squad => squad.Members.Count > 0 && RosterFormat.MatchesFilter(squad, _rosterFilter))
                    .GroupBy(squad => squad.ParentUnit))
                {
                    units.Add(new CommandTreeNode(
                        LoadedUnitKey(ship.Id, group.Key.Id),
                        $"{group.Key.Name} | {group.Sum(squad => squad.Members.Count)} aboard",
                        group.Select(squad => new CommandTreeNode(LoadedSquadKey(ship.Id, squad.Id), RosterFormat.SquadLabel(squad))).ToList()));
                }

                if (units.Count > 0 || _rosterFilter is "all" or "orbit")
                {
                    ships.Add(new CommandTreeNode(ShipKey(ship.Id), $"{ship.Name} ({ship.LoadedSoldierCount}/{ship.Template.SoldierCapacity})", units));
                }
            }
        }

        return ships;
    }

    private IReadOnlyList<CommandTreeNode> BuildSurfaceRegionNodes()
    {
        List<CommandTreeNode> regions = [];
        foreach (Region region in _selectedPlanet.Regions)
        {
            RegionFaction playerRegionFaction = GetPlayerRegionFaction(region);
            List<CommandTreeNode> units = [];
            if (playerRegionFaction != null)
            {
                foreach (IGrouping<Unit, Squad> group in playerRegionFaction.LandedSquads
                    .Where(squad => squad.Members.Count > 0 && RosterFormat.MatchesFilter(squad, _rosterFilter))
                    .GroupBy(squad => squad.ParentUnit))
                {
                    units.Add(new CommandTreeNode(
                        SurfaceUnitKey(region.Id, group.Key.Id),
                        $"{group.Key.Name} | {group.Sum(squad => squad.Members.Count)} on surface",
                        group.Select(squad => new CommandTreeNode(SurfaceSquadKey(region.Id, squad.Id), RosterFormat.SquadLabel(squad))).ToList()));
                }
            }

            if (units.Count > 0)
            {
                regions.Add(new CommandTreeNode(RegionKey(region.Id), region.Name, units));
            }
        }

        return regions;
    }

    private string GetContextTitle()
    {
        if (_selectedLoadedSquad != null) return _selectedLoadedSquad.Name;
        if (_selectedLandedSquad != null) return _selectedLandedSquad.Name;
        if (_selectedShip != null) return _selectedShip.Name;
        if (_selectedRegion != null) return _selectedRegion.Name;
        return _selectedPlanet?.Name ?? "Planet Detail";
    }

    private string GetContextSubtitle()
    {
        if (_selectedLoadedSquad != null) return $"Aboard {_selectedLoadedSquad.BoardedLocation?.Name ?? "unknown ship"}";
        if (_selectedLandedSquad != null) return $"Deployed in {_selectedLandedSquad.CurrentRegion?.Name ?? _selectedRegion?.Name ?? "unknown region"}";
        if (_selectedShip != null) return "Orbiting transport and combat capacity";
        if (_selectedRegion != null) return "Region summary; select a squad for order detail";
        return "Strategic planet summary";
    }

    private IReadOnlyList<Tuple<string, string>> BuildContextRows()
    {
        if (_selectedPlanet == null) return Array.Empty<Tuple<string, string>>();
        if (_selectedLoadedSquad != null) return BuildSquadRows(_selectedLoadedSquad);
        if (_selectedLandedSquad != null) return BuildSquadRows(_selectedLandedSquad);
        if (_selectedShip != null) return BuildShipRows(_selectedShip);
        if (_selectedRegion != null) return BuildRegionRows(_selectedRegion);
        return BuildPlanetRows(_selectedPlanet);
    }

    private IReadOnlyList<Tuple<string, string>> BuildPlanetRows(Planet planet)
    {
        List<Tuple<string, string>> rows = [];
        rows.Add(Row("Name", planet.Name));
        Faction controllingFaction = planet.GetControllingFaction();
        rows.Add(Row("Control", controllingFaction?.Name ?? "Unknown"));
        if (controllingFaction != null && (controllingFaction.IsDefaultFaction || controllingFaction.IsPlayerFaction))
        {
            rows.Add(Row("Classification", planet.Template.Name));
            rows.Add(Row("Population", planet.Population.ToString("N0")));
            rows.Add(Row("PDF Size", planet.PlanetaryDefenseForces.ToString("N0")));
            rows.Add(Row("Aestimare", ConvertImportanceToString(planet.Importance)));
            rows.Add(Row("Tithe Grade", ConvertTaxRangeToString(planet.TaxLevel)));
            Character governor = planet.PlanetFactionMap[controllingFaction.Id].Leader;
            if (governor != null)
            {
                rows.Add(Row("Governor", governor.Name));
                rows.Add(Row("Governor Opinion", ConvertOpinionToString(governor.OpinionOfPlayerForce)));
                rows.Add(Row("Active Request", governor.ActiveRequest != null ? "Yes" : "No"));
            }
        }
        else if (controllingFaction != null)
        {
            rows.Add(Row("Xenos Present", controllingFaction.Name));
        }

        rows.Add(Row("Regions", planet.Regions.Length.ToString()));
        rows.Add(Row("Orbiting Task Forces", planet.OrbitingTaskForceList.Count.ToString()));
        return rows;
    }

    private IReadOnlyList<Tuple<string, string>> BuildRegionRows(Region region)
    {
        List<Tuple<string, string>> rows = [];
        rows.Add(Row("Control", GetRegionControlLabel(region)));
        rows.Add(Row("Coordinates", $"({region.Coordinates.X}, {region.Coordinates.Y})"));
        float visibleIntel = region.GetPlayerVisibleIntel();
        rows.Add(Row("Intelligence", $"{visibleIntel:0.##}"));

        RegionFaction playerRegionFaction = GetPlayerRegionFaction(region);
        RegionFaction enemyFaction = GetEnemyRegionFaction(region);

        if (region.HasHiddenDefaultFaction())
        {
            rows.Add(Row("Civilian Population", "Unknown"));
        }
        else
        {
            long civilianPopulation = region.GetVisibleCivilianPopulation();
            rows.Add(Row("Civilian Population", civilianPopulation > 0 ? civilianPopulation.ToString("N0") : "None"));
        }
        rows.Add(Row("PDF Garrison", region.PlanetaryDefenseForces > 0 ? region.PlanetaryDefenseForces.ToString("N0") : "None"));
        rows.Add(Row("Marines", playerRegionFaction?.LandedSquads.Sum(squad => squad.Members.Count).ToString() ?? "0"));
        rows.Add(Row("Assigned Orders", playerRegionFaction?.LandedSquads.Count(squad => squad.CurrentOrders != null).ToString() ?? "0"));

        if (enemyFaction != null && enemyFaction.IsPublic)
        {
            rows.Add(Row("Enemy Faction", enemyFaction.PlanetFaction.Faction.Name));
            rows.Add(Row("Enemy Population", enemyFaction.GetPopulationDescription()));
            if (visibleIntel > 1)
            {
                rows.Add(Row("Enemy Entrenchment", RegionFactionExtensions.GetDefenseLevelDescription(enemyFaction.Entrenchment)));
                rows.Add(Row("Enemy Listening Posts", RegionFactionExtensions.GetDefenseLevelDescription(enemyFaction.ListeningPost)));
                rows.Add(Row("Enemy Anti-Air", RegionFactionExtensions.GetDefenseLevelDescription(enemyFaction.AntiAir)));
            }
        }
        else
        {
            rows.Add(Row("Enemy Presence", "None Detected"));
        }

        return rows;
    }

    private IReadOnlyList<Tuple<string, string>> BuildSquadRows(Squad squad)
    {
        List<Tuple<string, string>> rows = [];
        rows.Add(Row("Unit", squad.ParentUnit?.Name ?? "Unknown"));
        rows.Add(Row("Fighting Strength", $"{squad.Members.Count(member => member.CanFight)}/{squad.Members.Count}"));
        rows.Add(Row("Location", squad.BoardedLocation != null ? $"Aboard {squad.BoardedLocation.Name}" : squad.CurrentRegion?.Name ?? "Unknown"));
        rows.Add(Row("Orders", squad.CurrentOrders?.Mission.MissionType.ToString() ?? "Unassigned"));
        if (squad.CurrentOrders != null)
        {
            rows.Add(Row("Target Region", squad.CurrentOrders.Mission.RegionFaction.Region.Name));
            rows.Add(Row("Aggression", squad.CurrentOrders.LevelOfAggression.ToString()));
        }
        return rows;
    }

    private static IReadOnlyList<Tuple<string, string>> BuildShipRows(Ship ship)
    {
        return
        [
            Row("Loaded", $"{ship.LoadedSoldierCount}/{ship.Template.SoldierCapacity}"),
            Row("Available Capacity", ship.AvailableCapacity.ToString()),
            Row("Loaded Squads", ship.LoadedSquads.Count.ToString())
        ];
    }

    private IReadOnlyList<CommandAction> BuildCommands()
    {
        return
        [
            new(ActionOpenRegion, "Open Region", "map_pin", _selectedRegion != null),
            new(ActionOpenSquad, "Open Squad", "player_forces", GetSelectedSquad() != null),
            new(ActionEditOrders, "Edit Orders", "objective", _selectedLandedSquad != null),
            new(ActionLand, GetLandCommandText(), "land_squads", CanLand()),
            new(ActionLoad, GetLoadCommandText(), "load_squads", CanLoad())
        ];
    }

    private IReadOnlyList<IReadOnlyList<CommandAction>> BuildCommandRows()
    {
        IReadOnlyList<CommandAction> commands = BuildCommands();
        return
        [
            [commands[0], commands[1], commands[2]],
            [commands[3], commands[4]]
        ];
    }

    private void ApplySelectionKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith("group:") || key.StartsWith("presence:")) return;

        string[] parts = key.Split(':');
        switch (parts[0])
        {
            case "region":
                _selectedRegion = _selectedPlanet.Regions.FirstOrDefault(region => region.Id == int.Parse(parts[1]));
                _selectedLandedUnit = null;
                _selectedLandedSquad = null;
                break;
            case "ship":
                _selectedShip = FindShip(int.Parse(parts[1]));
                _selectedLoadedUnit = null;
                _selectedLoadedSquad = null;
                break;
            case "loaded-unit":
                _selectedShip = FindShip(int.Parse(parts[1]));
                _selectedLoadedUnit = GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle.ChildUnits.FirstOrDefault(unit => unit.Id == int.Parse(parts[2]));
                _selectedLoadedSquad = null;
                break;
            case "loaded-squad":
                _selectedShip = FindShip(int.Parse(parts[1]));
                _selectedLoadedSquad = GameDataSingleton.Instance.Sector.PlayerForce.Army.SquadMap[int.Parse(parts[2])];
                _selectedLoadedUnit = _selectedLoadedSquad.ParentUnit;
                break;
            case "surface-unit":
                _selectedRegion = _selectedPlanet.Regions.FirstOrDefault(region => region.Id == int.Parse(parts[1]));
                _selectedLandedUnit = GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle.ChildUnits.FirstOrDefault(unit => unit.Id == int.Parse(parts[2]));
                _selectedLandedSquad = null;
                break;
            case "surface-squad":
                _selectedRegion = _selectedPlanet.Regions.FirstOrDefault(region => region.Id == int.Parse(parts[1]));
                _selectedLandedSquad = GameDataSingleton.Instance.Sector.PlayerForce.Army.SquadMap[int.Parse(parts[2])];
                _selectedLandedUnit = _selectedLandedSquad.ParentUnit;
                break;
        }
    }

    private void LandSelectedForces()
    {
        if (!CanLand()) return;
        RegionFaction regionFaction = GetOrCreatePlayerRegionFaction(_selectedRegion);

        foreach (Squad squad in GetSelectedLoadedSquads().ToList())
        {
            Ship ship = squad.BoardedLocation;
            ship?.RemoveSquad(squad);
            if (!regionFaction.LandedSquads.Contains(squad))
            {
                regionFaction.LandedSquads.Add(squad);
            }
            squad.CurrentRegion = _selectedRegion;
            squad.BoardedLocation = null;
        }

        ClearForceSelections();
        RefreshWorkspace();
    }

    private void LoadSelectedForces()
    {
        if (!CanLoad()) return;
        RegionFaction regionFaction = GetPlayerRegionFaction(_selectedRegion);
        if (regionFaction == null) return;

        foreach (Squad squad in GetSelectedLandedSquads().ToList())
        {
            regionFaction.LandedSquads.Remove(squad);
            _selectedShip.LoadSquad(squad);
            squad.CurrentRegion = null;
            squad.BoardedLocation = _selectedShip;
        }

        CleanupPlayerRegionFactionAfterLoad(_selectedRegion, regionFaction);
        ClearForceSelections();
        RefreshWorkspace();
    }

    private bool CanLand()
    {
        return _selectedShip != null
            && _selectedRegion != null
            && _selectedRegion.Planet != null
            && GameDataSingleton.Instance?.Sector?.PlayerForce?.Faction != null
            && GetSelectedLoadedSquads().Any();
    }

    private bool CanLoad()
    {
        if (_selectedShip == null || _selectedRegion == null) return false;
        IEnumerable<Squad> squads = GetSelectedLandedSquads();
        int capacityRequired = squads.Sum(squad => squad.Members.Count);
        return capacityRequired > 0 && capacityRequired <= _selectedShip.AvailableCapacity;
    }

    private string GetLandCommandText()
    {
        if (_selectedLoadedSquad != null) return $"Land {_selectedLoadedSquad.Name}";
        if (_selectedLoadedUnit != null) return $"Land {_selectedLoadedUnit.Name}";
        if (_selectedShip != null) return $"Land From {_selectedShip.Name}";
        return "Land Selected";
    }

    private string GetLoadCommandText()
    {
        if (_selectedLandedSquad != null) return $"Load {_selectedLandedSquad.Name}";
        if (_selectedLandedUnit != null) return $"Load {_selectedLandedUnit.Name}";
        if (_selectedRegion != null) return $"Load From {_selectedRegion.Name}";
        return "Load Selected";
    }

    private IEnumerable<Squad> GetSelectedLoadedSquads()
    {
        if (_selectedLoadedSquad != null) return [_selectedLoadedSquad];
        if (_selectedShip == null) return [];
        if (_selectedLoadedUnit != null)
        {
            return _selectedShip.LoadedSquads.Where(squad => squad.ParentUnit == _selectedLoadedUnit);
        }
        return _selectedShip.LoadedSquads;
    }

    private IEnumerable<Squad> GetSelectedLandedSquads()
    {
        if (_selectedRegion == null) return [];
        RegionFaction regionFaction = GetPlayerRegionFaction(_selectedRegion);
        if (regionFaction == null) return [];
        if (_selectedLandedSquad != null) return [_selectedLandedSquad];
        if (_selectedLandedUnit != null)
        {
            return regionFaction.LandedSquads.Where(squad => squad.ParentUnit == _selectedLandedUnit);
        }
        return regionFaction.LandedSquads;
    }

    private Squad GetSelectedSquad()
    {
        return _selectedLoadedSquad ?? _selectedLandedSquad;
    }

    private Ship FindShip(int shipId)
    {
        return _selectedPlanet?.OrbitingTaskForceList.SelectMany(taskForce => taskForce.Ships).FirstOrDefault(ship => ship.Id == shipId);
    }

    private void ClearForceSelections()
    {
        _selectedShip = null;
        _selectedLoadedUnit = null;
        _selectedLoadedSquad = null;
        _selectedLandedUnit = null;
        _selectedLandedSquad = null;
    }

    private void RefreshRegionMap()
    {
        if (_selectedPlanet == null) return;
        for (int i = 0; i < _selectedPlanet.Regions.Length; i++)
        {
            Region region = _selectedPlanet.Regions[i];
            _tacticalRegions[i].Populate(region, _activeLayers, _selectedRegion != null && region.Id == _selectedRegion.Id);
        }
    }

    private RegionFaction GetPlayerRegionFaction(Region region)
    {
        if (region == null) return null;
        Faction playerFaction = GameDataSingleton.Instance.Sector.PlayerForce.Faction;
        region.RegionFactionMap.TryGetValue(playerFaction.Id, out RegionFaction regionFaction);
        return regionFaction;
    }

    private RegionFaction GetOrCreatePlayerRegionFaction(Region region)
    {
        Faction playerFaction = GameDataSingleton.Instance.Sector.PlayerForce.Faction;
        if (!region.Planet.PlanetFactionMap.TryGetValue(playerFaction.Id, out PlanetFaction playerPlanetFaction))
        {
            playerPlanetFaction = new PlanetFaction(playerFaction) { IsPublic = true };
            region.Planet.PlanetFactionMap[playerFaction.Id] = playerPlanetFaction;
        }

        if (!region.RegionFactionMap.TryGetValue(playerFaction.Id, out RegionFaction regionFaction))
        {
            regionFaction = new RegionFaction(playerPlanetFaction, region) { IsPublic = true };
            region.RegionFactionMap[playerFaction.Id] = regionFaction;
        }

        return regionFaction;
    }

    private static void CleanupPlayerRegionFactionAfterLoad(Region region, RegionFaction regionFaction)
    {
        if (region == null || regionFaction == null || regionFaction.LandedSquads.Count > 0) return;

        if (regionFaction.Entrenchment <= 0
            && regionFaction.ListeningPost <= 0
            && regionFaction.AntiAir <= 0)
        {
            region.RegionFactionMap.Remove(regionFaction.PlanetFaction.Faction.Id);
            return;
        }

        regionFaction.IsPublic = false;
    }

    private static RegionFaction GetEnemyRegionFaction(Region region)
    {
        return region.GetVisibleEnemyRegionFaction();
    }

    private static string GetRegionControlLabel(Region region)
    {
        return region.ControllingFaction?.PlanetFaction.Faction.Name ?? "Contested";
    }

    private static Tuple<string, string> Row(string label, string value)
    {
        return new Tuple<string, string>(label, value);
    }

    private static string RegionKey(int regionId) => $"region:{regionId}";
    private static string ShipKey(int shipId) => $"ship:{shipId}";
    private static string LoadedUnitKey(int shipId, int unitId) => $"loaded-unit:{shipId}:{unitId}";
    private static string LoadedSquadKey(int shipId, int squadId) => $"loaded-squad:{shipId}:{squadId}";
    private static string SurfaceUnitKey(int regionId, int unitId) => $"surface-unit:{regionId}:{unitId}";
    private static string SurfaceSquadKey(int regionId, int squadId) => $"surface-squad:{regionId}:{squadId}";

    private static string ConvertOpinionToString(float opinion)
    {
        if (opinion < -1f / 3f) return "Hostile";
        if (opinion > 1f / 3f) return "Friendly";
        return "Neutral";
    }

    private static string ConvertImportanceToString(int importance)
    {
        if (importance > 6000) return $"G{importance % 1000}";
        if (importance > 5000) return $"F{importance % 1000}";
        if (importance > 4000) return $"E{importance % 1000}";
        if (importance > 3000) return $"D{importance % 1000}";
        if (importance > 2000) return $"C{importance % 1000}";
        if (importance > 1000) return $"B{importance % 1000}";
        return $"A{importance}";
    }

    private static string ConvertTaxRangeToString(int taxRate)
    {
        return taxRate switch
        {
            0 => "Adeptus Non",
            1 => "Solutio Tertius",
            2 => "Solutio Secundus",
            3 => "Solutio Prima",
            4 => "Solutio Particular",
            5 => "Solutio Extremis",
            6 => "Decuma Tertius",
            7 => "Decuma Secundus",
            8 => "Decuma Prima",
            9 => "Decuma Particular",
            10 => "Decuma Extremis",
            11 => "Exactis Tertius",
            12 => "Exactis Secundus",
            13 => "Exactis Prima",
            14 => "Exactis Median",
            15 => "Exactis Particular",
            16 => "Exactis Extremis",
            _ => ""
        };
    }
}
