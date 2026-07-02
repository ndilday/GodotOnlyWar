using Godot;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Orders;
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
    private const string ActionOpenOrders = "open_orders";

    private PlanetTacticalScreenView _view;
    private TacticalRegionController[] _tacticalRegions;
    private ButtonGroup _buttonGroup;
    private Planet _selectedPlanet;
    private PlanetCommandMode _mode = PlanetCommandMode.Overview;
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
        _view.ModeSelected += OnModeSelected;
        _view.SelectionTreeItemSelected += OnSelectionTreeItemSelected;
        _view.SelectionTreeItemActivated += OnSelectionTreeItemActivated;
        _view.CommandPressed += OnCommandPressed;

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

    private void OnModeSelected(object sender, PlanetCommandMode mode)
    {
        _mode = mode;
        ClearForceSelections();
        _view.SetMode(mode);
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
            case ActionOpenOrders:
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
            case ActionLand:
                LandSelectedForces();
                break;
            case ActionLoad:
                LoadSelectedForces();
                break;
        }
    }

    private void RefreshWorkspace()
    {
        RefreshRegionMap();
        _view.SetMode(_mode);
        _view.SetSelectionTitle(GetSelectionTitle(), GetSelectionHint());
        _view.PopulateSelectionTree(BuildSelectionTree());
        RefreshContextAndCommands();
    }

    private void RefreshContextAndCommands()
    {
        RefreshRegionMap();
        _view.SetContext(GetContextTitle(), GetContextSubtitle(), BuildContextRows());
        _view.SetCommands(BuildCommands());
    }

    private string GetSelectionTitle()
    {
        return _mode switch
        {
            PlanetCommandMode.Overview => "REGIONS",
            PlanetCommandMode.Forces => "FORCE LOCATIONS",
            PlanetCommandMode.Orders => "ORDERS",
            PlanetCommandMode.Logistics => "ORBIT / SURFACE",
            PlanetCommandMode.Intel => "INTEL CONTACTS",
            _ => "SELECTIONS"
        };
    }

    private string GetSelectionHint()
    {
        return _mode switch
        {
            PlanetCommandMode.Overview => "Select a region on the map or list to inspect it.",
            PlanetCommandMode.Forces => "Select a force to locate it; double-click squads for detail.",
            PlanetCommandMode.Orders => "Select an order or squad; open the region screen to edit orders.",
            PlanetCommandMode.Logistics => "Select a ship and a region or surface squad, then land or load.",
            PlanetCommandMode.Intel => "Select a region to inspect known enemy strength and defenses.",
            _ => ""
        };
    }

    private IReadOnlyList<PlanetCommandTreeNode> BuildSelectionTree()
    {
        if (_selectedPlanet == null) return Array.Empty<PlanetCommandTreeNode>();

        return _mode switch
        {
            PlanetCommandMode.Forces => BuildForcesTree(),
            PlanetCommandMode.Orders => BuildOrdersTree(),
            PlanetCommandMode.Logistics => BuildLogisticsTree(),
            PlanetCommandMode.Intel => BuildIntelTree(),
            _ => BuildRegionTree()
        };
    }

    private IReadOnlyList<PlanetCommandTreeNode> BuildRegionTree()
    {
        return _selectedPlanet.Regions
            .Select(region => new PlanetCommandTreeNode(RegionKey(region.Id), $"{region.Name} | {GetRegionControlLabel(region)}"))
            .ToList();
    }

    private IReadOnlyList<PlanetCommandTreeNode> BuildForcesTree()
    {
        List<PlanetCommandTreeNode> roots = [];
        roots.Add(new PlanetCommandTreeNode("group:orbit", "In Orbit", BuildOrbitShipNodes(includeEmptyShips: true)));

        List<PlanetCommandTreeNode> regionNodes = [];
        foreach (Region region in _selectedPlanet.Regions)
        {
            List<PlanetCommandTreeNode> children = [];
            RegionFaction playerRegionFaction = GetPlayerRegionFaction(region);
            if (playerRegionFaction != null)
            {
                foreach (IGrouping<Unit, Squad> group in playerRegionFaction.LandedSquads
                    .Where(squad => squad.Members.Count > 0)
                    .GroupBy(squad => squad.ParentUnit))
                {
                    children.Add(new PlanetCommandTreeNode(
                        SurfaceUnitKey(region.Id, group.Key.Id),
                        $"{group.Key.Name} | {group.Sum(squad => squad.Members.Count)} marines",
                        group.Select(squad => new PlanetCommandTreeNode(SurfaceSquadKey(region.Id, squad.Id), squad.Name)).ToList()));
                }
            }

            foreach (RegionFaction faction in region.RegionFactionMap.Values.Where(rf => rf.IsPublic && !rf.PlanetFaction.Faction.IsPlayerFaction))
            {
                string role = faction.PlanetFaction.Faction.IsDefaultFaction ? "Allied/PDF" : "Enemy";
                string strength = faction.PlanetFaction.Faction.IsDefaultFaction
                    ? $"{faction.Garrison:N0} garrison"
                    : faction.GetPopulationDescription();
                children.Add(new PlanetCommandTreeNode($"presence:{region.Id}:{faction.PlanetFaction.Faction.Id}", $"{role}: {faction.PlanetFaction.Faction.Name} | {strength}"));
            }

            if (children.Count > 0)
            {
                regionNodes.Add(new PlanetCommandTreeNode(RegionKey(region.Id), region.Name, children));
            }
        }
        roots.Add(new PlanetCommandTreeNode("group:surface", "Surface Regions", regionNodes));
        return roots;
    }

    private IReadOnlyList<PlanetCommandTreeNode> BuildOrdersTree()
    {
        List<PlanetCommandTreeNode> ordered = [];
        List<PlanetCommandTreeNode> unassigned = [];
        foreach (Region region in _selectedPlanet.Regions)
        {
            RegionFaction playerRegionFaction = GetPlayerRegionFaction(region);
            if (playerRegionFaction == null) continue;

            foreach (Squad squad in playerRegionFaction.LandedSquads.Where(squad => squad.Members.Count > 0))
            {
                if (squad.CurrentOrders == null)
                {
                    unassigned.Add(new PlanetCommandTreeNode(SurfaceSquadKey(region.Id, squad.Id), $"{squad.Name} | {region.Name}"));
                    continue;
                }

                Order order = squad.CurrentOrders;
                string orderKey = $"order:{order.Id}:{region.Id}";
                PlanetCommandTreeNode existing = ordered.FirstOrDefault(node => node.Key == orderKey);
                PlanetCommandTreeNode squadNode = new(SurfaceSquadKey(region.Id, squad.Id), $"{squad.Name} | {squad.ParentUnit?.Name ?? "Unknown Unit"}");
                if (existing == null)
                {
                    ordered.Add(new PlanetCommandTreeNode(orderKey, $"{order.Mission.MissionType} | {order.Mission.RegionFaction.Region.Name}", [squadNode]));
                }
                else
                {
                    List<PlanetCommandTreeNode> children = existing.Children.ToList();
                    children.Add(squadNode);
                    ordered[ordered.IndexOf(existing)] = new PlanetCommandTreeNode(existing.Key, existing.Text, children);
                }
            }
        }

        return
        [
            new PlanetCommandTreeNode("group:ordered", "Assigned", ordered),
            new PlanetCommandTreeNode("group:unassigned", "Unassigned", unassigned)
        ];
    }

    private IReadOnlyList<PlanetCommandTreeNode> BuildLogisticsTree()
    {
        return
        [
            new PlanetCommandTreeNode("group:orbit", "Orbiting Ships", BuildOrbitShipNodes(includeEmptyShips: true)),
            new PlanetCommandTreeNode("group:surface", "Surface Regions", BuildSurfaceRegionNodes(includeEmptyRegions: true))
        ];
    }

    private IReadOnlyList<PlanetCommandTreeNode> BuildIntelTree()
    {
        List<PlanetCommandTreeNode> publicContacts = [];
        List<PlanetCommandTreeNode> quietRegions = [];
        foreach (Region region in _selectedPlanet.Regions)
        {
            RegionFaction enemyFaction = GetEnemyRegionFaction(region);
            if (enemyFaction != null && enemyFaction.IsPublic)
            {
                publicContacts.Add(new PlanetCommandTreeNode(RegionKey(region.Id), $"{region.Name} | {enemyFaction.GetPopulationDescription()}"));
            }
            else
            {
                quietRegions.Add(new PlanetCommandTreeNode(RegionKey(region.Id), $"{region.Name} | no public contact"));
            }
        }

        return
        [
            new PlanetCommandTreeNode("group:contacts", "Public Enemy Contacts", publicContacts),
            new PlanetCommandTreeNode("group:quiet", "No Public Contact", quietRegions)
        ];
    }

    private IReadOnlyList<PlanetCommandTreeNode> BuildOrbitShipNodes(bool includeEmptyShips)
    {
        List<PlanetCommandTreeNode> ships = [];
        Faction playerFaction = GameDataSingleton.Instance.Sector.PlayerForce.Faction;
        foreach (TaskForce taskForce in _selectedPlanet.OrbitingTaskForceList.Where(tf => tf.Faction == playerFaction))
        {
            foreach (Ship ship in taskForce.Ships)
            {
                if (!includeEmptyShips && !ship.LoadedSquads.Any()) continue;

                List<PlanetCommandTreeNode> units = [];
                foreach (IGrouping<Unit, Squad> group in ship.LoadedSquads
                    .Where(squad => squad.Members.Count > 0)
                    .GroupBy(squad => squad.ParentUnit))
                {
                    units.Add(new PlanetCommandTreeNode(
                        LoadedUnitKey(ship.Id, group.Key.Id),
                        $"{group.Key.Name} | {group.Sum(squad => squad.Members.Count)} aboard",
                        group.Select(squad => new PlanetCommandTreeNode(LoadedSquadKey(ship.Id, squad.Id), squad.Name)).ToList()));
                }

                ships.Add(new PlanetCommandTreeNode(ShipKey(ship.Id), $"{ship.Name} ({ship.LoadedSoldierCount}/{ship.Template.SoldierCapacity})", units));
            }
        }

        return ships;
    }

    private IReadOnlyList<PlanetCommandTreeNode> BuildSurfaceRegionNodes(bool includeEmptyRegions)
    {
        List<PlanetCommandTreeNode> regions = [];
        foreach (Region region in _selectedPlanet.Regions)
        {
            RegionFaction playerRegionFaction = GetPlayerRegionFaction(region);
            List<PlanetCommandTreeNode> units = [];
            if (playerRegionFaction != null)
            {
                foreach (IGrouping<Unit, Squad> group in playerRegionFaction.LandedSquads
                    .Where(squad => squad.Members.Count > 0)
                    .GroupBy(squad => squad.ParentUnit))
                {
                    units.Add(new PlanetCommandTreeNode(
                        SurfaceUnitKey(region.Id, group.Key.Id),
                        $"{group.Key.Name} | {group.Sum(squad => squad.Members.Count)} on surface",
                        group.Select(squad => new PlanetCommandTreeNode(SurfaceSquadKey(region.Id, squad.Id), squad.Name)).ToList()));
                }
            }

            if (includeEmptyRegions || units.Count > 0)
            {
                regions.Add(new PlanetCommandTreeNode(RegionKey(region.Id), region.Name, units));
            }
        }

        return regions;
    }

    private string GetContextTitle()
    {
        if (_selectedLoadedSquad != null) return _selectedLoadedSquad.Name;
        if (_selectedLandedSquad != null) return _selectedLandedSquad.Name;
        if (_selectedShip != null && _mode == PlanetCommandMode.Logistics) return _selectedShip.Name;
        if (_selectedRegion != null) return _selectedRegion.Name;
        return _selectedPlanet?.Name ?? "Planet Detail";
    }

    private string GetContextSubtitle()
    {
        if (_selectedLoadedSquad != null) return $"Aboard {_selectedLoadedSquad.BoardedLocation?.Name ?? "unknown ship"}";
        if (_selectedLandedSquad != null) return $"Deployed in {_selectedLandedSquad.CurrentRegion?.Name ?? _selectedRegion?.Name ?? "unknown region"}";
        if (_selectedShip != null && _mode == PlanetCommandMode.Logistics) return "Orbiting transport and combat capacity";
        return _mode switch
        {
            PlanetCommandMode.Overview => "Strategic planet and selected-region summary",
            PlanetCommandMode.Forces => "Known friendly, allied, and enemy presence",
            PlanetCommandMode.Orders => "Current mission assignments and order groups",
            PlanetCommandMode.Logistics => "Move squads between orbiting ships and the selected region",
            PlanetCommandMode.Intel => "Detected threats, defenses, and intelligence quality",
            _ => ""
        };
    }

    private IReadOnlyList<Tuple<string, string>> BuildContextRows()
    {
        if (_selectedPlanet == null) return Array.Empty<Tuple<string, string>>();
        if (_selectedLoadedSquad != null) return BuildSquadRows(_selectedLoadedSquad);
        if (_selectedLandedSquad != null) return BuildSquadRows(_selectedLandedSquad);
        if (_selectedShip != null && _mode == PlanetCommandMode.Logistics) return BuildShipRows(_selectedShip);
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
        rows.Add(Row("Intelligence", $"{region.IntelligenceLevel:0.##}"));

        RegionFaction playerRegionFaction = GetPlayerRegionFaction(region);
        RegionFaction defaultFaction = region.RegionFactionMap.Values.FirstOrDefault(rf => rf.PlanetFaction.Faction.IsDefaultFaction);
        RegionFaction enemyFaction = GetEnemyRegionFaction(region);

        long civilianPopulation = (defaultFaction?.Population ?? 0) + (playerRegionFaction?.Population ?? 0);
        if (enemyFaction != null && !enemyFaction.IsPublic)
        {
            civilianPopulation += enemyFaction.Population;
        }
        rows.Add(Row("Civilian Population", civilianPopulation > 0 ? civilianPopulation.ToString("N0") : "None"));
        rows.Add(Row("Marines", playerRegionFaction?.LandedSquads.Sum(squad => squad.Members.Count).ToString() ?? "0"));
        rows.Add(Row("Assigned Orders", playerRegionFaction?.LandedSquads.Count(squad => squad.CurrentOrders != null).ToString() ?? "0"));

        if (enemyFaction != null && enemyFaction.IsPublic)
        {
            rows.Add(Row("Enemy Faction", enemyFaction.PlanetFaction.Faction.Name));
            rows.Add(Row("Enemy Population", enemyFaction.GetPopulationDescription()));
            if (region.IntelligenceLevel > 1)
            {
                rows.Add(Row("Enemy Entrenchment", RegionFactionExtensions.GetDefenseLevelDescription(enemyFaction.Entrenchment)));
                rows.Add(Row("Enemy Detection", RegionFactionExtensions.GetDefenseLevelDescription(enemyFaction.Detection)));
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

    private IReadOnlyList<PlanetCommandAction> BuildCommands()
    {
        List<PlanetCommandAction> actions =
        [
            new(ActionOpenRegion, "Open Region", "map_pin", _selectedRegion != null),
            new(ActionOpenSquad, "Open Squad", "infantry", GetSelectedSquad() != null)
        ];

        if (_mode == PlanetCommandMode.Orders)
        {
            actions.Add(new PlanetCommandAction(ActionOpenOrders, "Open Orders", "objective", _selectedRegion != null));
        }

        if (_mode == PlanetCommandMode.Logistics)
        {
            actions.Add(new PlanetCommandAction(ActionLand, GetLandCommandText(), "land_squads", CanLand()));
            actions.Add(new PlanetCommandAction(ActionLoad, GetLoadCommandText(), "load_squads", CanLoad()));
        }

        return actions;
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
            case "order":
                _selectedRegion = _selectedPlanet.Regions.FirstOrDefault(region => region.Id == int.Parse(parts[2]));
                _selectedLandedUnit = null;
                _selectedLandedSquad = null;
                break;
        }
    }

    private void LandSelectedForces()
    {
        if (!CanLand()) return;
        RegionFaction regionFaction = GetPlayerRegionFaction(_selectedRegion);
        if (regionFaction == null) return;

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

        ClearForceSelections();
        RefreshWorkspace();
    }

    private bool CanLand()
    {
        return _selectedShip != null
            && _selectedRegion != null
            && GetPlayerRegionFaction(_selectedRegion) != null
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
            _tacticalRegions[i].Populate(region, _mode, _selectedRegion != null && region.Id == _selectedRegion.Id);
        }
    }

    private RegionFaction GetPlayerRegionFaction(Region region)
    {
        if (region == null) return null;
        Faction playerFaction = GameDataSingleton.Instance.Sector.PlayerForce.Faction;
        region.RegionFactionMap.TryGetValue(playerFaction.Id, out RegionFaction regionFaction);
        return regionFaction;
    }

    private static RegionFaction GetEnemyRegionFaction(Region region)
    {
        return region.RegionFactionMap.Values.FirstOrDefault(rf => !rf.PlanetFaction.Faction.IsPlayerFaction && !rf.PlanetFaction.Faction.IsDefaultFaction);
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
