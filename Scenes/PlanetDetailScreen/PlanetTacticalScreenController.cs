using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fortifications;
using OnlyWar.Helpers.Orders;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Supply;
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

	private Planet _selectedPlanet;
	private MapLayer _activeLayers = MapLayer.Forces | MapLayer.Orders;
	private string _rosterFilter = "all";
	// Map selection is the landing destination. Surface selection is the embark source;
	// keeping them separate makes map clicks irrelevant to Embark eligibility.
	private Region _selectedRegion;
	// Map focus controls the dossier subject independently from force selection. A ship or
	// squad may stay selected as the source of a Land command while the clicked region's
	// information is shown in the right-hand panel.
	private bool _regionContextFocused = true;
	private Ship _selectedShip;
	private Squad _selectedLoadedSquad;
	private Region _selectedSurfaceRegion;
	private Squad _selectedLandedSquad;
	// The roster Tree is multi-select, so it - not the single-selection fields above - is
	// authoritative for which squads Land and Embark act on. Those fields still drive the dossier
	// context panel from the most recently clicked row.
	private IReadOnlyList<string> _selectedRosterKeys = [];
	private readonly List<Squad> _selectedLoadedSquads = [];
	private readonly List<Squad> _selectedLandedSquads = [];
	private PopupMenu _embarkShipMenu;
	private LoadoutDoctrineDialog _loadoutDoctrineDialog;

	public event EventHandler<Region> RegionDoubleClicked;
	public event EventHandler<Squad> OrbitalSquadDoubleClicked;
	public event EventHandler CampaignChanged;

	public override void _Ready()
	{
		base._Ready();
		_buttonGroup = new ButtonGroup();
		_view = GetNode<PlanetTacticalScreenView>("DialogView/PlanetTacticalScreenView");
		_view.SelectionTreeItemSelected += OnSelectionTreeItemSelected;
		_view.SelectionTreeItemActivated += OnSelectionTreeItemActivated;
		_view.CommandPressed += OnCommandPressed;
		_view.MapLayerToggled += OnMapLayerToggled;
		_view.RosterFilterSelected += OnRosterFilterSelected;
		_view.TheaterLoadoutsPressed += OnTheaterLoadoutsPressed;
		_view.SetMapLayerOptions(MapLayerOptions);
		_view.SetActiveMapLayers(_activeLayers);
		_view.SetRosterFilters(RosterFilters);
		_view.SetActiveRosterFilter(_rosterFilter);
		// Ctrl/shift-click accumulates a landing or embarkation force across ships, units, and
		// individual squads; a plain click still replaces the selection with a single row.
		_view.SetSelectionMultiSelect(true);

		_embarkShipMenu = new PopupMenu
		{
			Title = "Choose Transport"
		};
		_embarkShipMenu.IdPressed += OnEmbarkShipSelected;
		AddChild(_embarkShipMenu);

		_loadoutDoctrineDialog = new LoadoutDoctrineDialog();
		_loadoutDoctrineDialog.DoctrineChanged += (_, _) =>
		{
			CampaignChanged?.Invoke(this, EventArgs.Empty);
			RefreshWorkspace();
		};
		AddChild(_loadoutDoctrineDialog);

		_tacticalRegions = new TacticalRegionController[16];
		for (int i = 1; i <= 16; i++)
		{
			_tacticalRegions[i - 1] = GetNode<TacticalRegionController>($"DialogView/PlanetTacticalScreenView/TacticalRegionPanel/TacticalRegionController{i}");
			_tacticalRegions[i - 1].AddToButtonGroup(_buttonGroup);
			_tacticalRegions[i - 1].TacticalRegionPressed += OnTacticalRegionPressed;
			_tacticalRegions[i - 1].TacticalRegionDoubleClicked += OnTacticalRegionDoubleClicked;
		}
	}

	private void OnTheaterLoadoutsPressed(object sender, EventArgs e)
	{
		PlayerForce force = GameDataSingleton.Instance?.Sector?.PlayerForce;
		if (force != null && _selectedPlanet != null)
		{
			_loadoutDoctrineDialog.OpenPlanet(force, _selectedPlanet);
		}
	}

	public override void _ExitTree()
	{
		if (_embarkShipMenu != null)
		{
			_embarkShipMenu.IdPressed -= OnEmbarkShipSelected;
		}
	}

	public void PopulatePlanetData(Planet planet)
	{
		_selectedPlanet = planet;
		_selectedRegion = planet?.Regions.FirstOrDefault();
		_regionContextFocused = true;
		ClearForceSelections();

		if (planet != null)
		{
			RefreshRegionMap();
		}

		RefreshWorkspace();
	}

	public void FocusRegion(Region region)
	{
		if (_selectedPlanet == null || region == null || region.Planet != _selectedPlanet)
		{
			return;
		}

		_selectedRegion = _selectedPlanet.Regions.FirstOrDefault(candidate => candidate.Id == region.Id);
		if (_selectedRegion == null)
		{
			return;
		}

		_regionContextFocused = true;
		RefreshContextAndCommands();
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
		_regionContextFocused = true;
		// Rebuilding the roster here restores its selected ship/squad row, and Godot emits
		// ItemSelected while doing so. That immediately steals context focus back from the
		// clicked map region. A map click changes only the destination and dossier context;
		// the roster itself has not changed and must not be rebuilt.
		RefreshContextAndCommands();
	}

	private void OnTacticalRegionDoubleClicked(object sender, Region region)
	{
		if (region == null) return;

		_selectedRegion = region;
		RegionDoubleClicked?.Invoke(this, region);
	}

	private void OnSelectionTreeItemSelected(object sender, string key)
	{
		RecomputeSelectedSquads();
		ApplySelectionKey(ResolveContextKey(key));
		RefreshContextAndCommands();
	}

	private void OnSelectionTreeItemActivated(object sender, string key)
	{
		RecomputeSelectedSquads();
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
			case ActionLand:
				LandSelectedForces();
				break;
			case ActionLoad:
				ShowEmbarkShipMenu();
				break;
		}
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
		_view.PopulateSelectionTree(BuildRoster());
		// PopulateSelectionTree restores the previous selection by key, so the squad sets have to be
		// rebuilt against the freshly created rows before any command text is derived from them.
		RecomputeSelectedSquads();
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
		IRequest request = governor?.ActiveRequest;
		if (request == null) return null;

		// "Governor request pending" said only that something existed somewhere, which is what
		// made this a dead end. Name the deadline so the badge carries the one fact that decides
		// whether the player needs to act now; the dossier card below carries the terms.
		return $"Governor's request — due {FormatRequestDate(request.Deadline)}";
	}

	private void RefreshContextAndCommands()
	{
		RefreshRegionMap();
		RefreshSelectionSummary();
		_view.SetContextCards(GetContextTitle(), GetContextSubtitle(), BuildContextCards());
		_view.SetCommandRows(BuildCommandRows());
	}

	private void RefreshSelectionSummary()
	{
		int count = _selectedLoadedSquads.Count + _selectedLandedSquads.Count;
		string hint = count == 0
			? "Select a region, ship, or squad; ctrl-click to select several. Land, load, or open its orders from the command bar."
			: $"{count} squad{(count == 1 ? "" : "s")} selected · ctrl-click to add or remove";
		_view.SetSelectionTitle("ROSTER", hint);
	}

	private IReadOnlyList<CommandTreeNode> BuildRoster()
	{
		if (_selectedPlanet == null) return Array.Empty<CommandTreeNode>();

		List<CommandTreeNode> roots = [];
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

	private IReadOnlyList<CommandTreeNode> BuildOrbitShipNodes()
	{
		List<CommandTreeNode> ships = [];
		Faction playerFaction = GameDataSingleton.Instance.Sector.PlayerForce.Faction;
		foreach (TaskForce taskForce in _selectedPlanet.OrbitingTaskForceList
			.Where(tf => tf.Faction == playerFaction)
			.OrderBy(taskForce => taskForce.Id))
		{
			foreach (Ship ship in taskForce.Ships
				.OrderByDescending(ship => ship.Template.SoldierCapacity)
				.ThenBy(ship => ship.Template.Id)
				.ThenBy(ship => ship.Name)
				.ThenBy(ship => ship.Id))
			{
				IReadOnlyList<CommandTreeNode> units = CreateLoadedUnitNodes(ship, _rosterFilter);
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
			IReadOnlyList<CommandTreeNode> units = CreateSurfaceUnitNodes(region.Id, playerRegionFaction, _rosterFilter);

			if (units.Count > 0)
			{
				regions.Add(new CommandTreeNode(RegionKey(region.Id), region.Name, units));
			}
		}

		return regions;
	}

	internal static IReadOnlyList<CommandTreeNode> CreateLoadedUnitNodes(Ship ship, string rosterFilter)
	{
		return ship.LoadedSquads
			.Where(squad => squad.IsOperational
				&& squad.Members.Count > 0
				&& RosterFormat.MatchesFilter(squad, rosterFilter))
			.OrderBy(squad => FleetScreenController.GetUnitOrderKey(squad.ParentUnit))
			.ThenBy(squad => FleetScreenController.GetSquadTypeOrder(squad))
			.ThenBy(squad => squad.Name, StringComparer.OrdinalIgnoreCase)
			.ThenBy(squad => squad.Id)
			.GroupBy(squad => squad.ParentUnit)
			.Select(group =>
			{
				Unit unit = group.Key;
				int unitId = unit?.Id ?? 0;
				string unitName = unit?.Name ?? "Unassigned Unit";
				return new CommandTreeNode(
					LoadedUnitKey(ship.Id, unitId),
					$"{unitName} | {group.Sum(squad => squad.Members.Count)} aboard",
					group.Select(squad => new CommandTreeNode(
						LoadedSquadKey(ship.Id, squad.Id),
						RosterFormat.SquadLabel(squad),
						null,
						IconAtlas.GetSquadIconKey(squad.SquadTemplate),
						null)).ToList());
			})
			.ToList();
	}

	internal static IReadOnlyList<CommandTreeNode> CreateSurfaceUnitNodes(int regionId, RegionFaction playerRegionFaction, string rosterFilter)
	{
		if (playerRegionFaction == null) return Array.Empty<CommandTreeNode>();

		return playerRegionFaction.LandedSquads
			.Where(squad => squad.IsOperational
				&& squad.Members.Count > 0
				&& RosterFormat.MatchesFilter(squad, rosterFilter))
			.OrderBy(squad => FleetScreenController.GetUnitOrderKey(squad.ParentUnit))
			.ThenBy(squad => FleetScreenController.GetSquadTypeOrder(squad))
			.ThenBy(squad => squad.Name, StringComparer.OrdinalIgnoreCase)
			.ThenBy(squad => squad.Id)
			.GroupBy(squad => squad.ParentUnit)
			.Select(group =>
			{
				Unit unit = group.Key;
				int unitId = unit?.Id ?? 0;
				string unitName = unit?.Name ?? "Unassigned Unit";
				return new CommandTreeNode(
					SurfaceUnitKey(regionId, unitId),
					$"{unitName} | {group.Sum(squad => squad.Members.Count)} on surface",
					group.Select(squad => new CommandTreeNode(
						SurfaceSquadKey(regionId, squad.Id),
						RosterFormat.SquadLabel(squad),
						null,
						IconAtlas.GetSquadIconKey(squad.SquadTemplate),
						null)).ToList());
			})
			.ToList();
	}

	private string GetContextTitle()
	{
		if (_regionContextFocused && _selectedRegion != null) return _selectedRegion.Name;
		if (_selectedLoadedSquad != null) return _selectedLoadedSquad.Name;
		if (_selectedLandedSquad != null) return _selectedLandedSquad.Name;
		if (_selectedShip != null) return _selectedShip.Name;
		if (_selectedRegion != null) return _selectedRegion.Name;
		return _selectedPlanet?.Name ?? "Planet Detail";
	}

	private string GetContextSubtitle()
	{
		if (_regionContextFocused && _selectedRegion != null) return "Region summary; select a squad for order detail";
		if (_selectedLoadedSquad != null) return $"Aboard {_selectedLoadedSquad.BoardedLocation?.Name ?? "unknown ship"}";
		if (_selectedLandedSquad != null) return $"Deployed in {_selectedLandedSquad.CurrentRegion?.Name ?? _selectedRegion?.Name ?? "unknown region"}";
		if (_selectedShip != null) return "Orbiting transport and combat capacity";
		if (_selectedRegion != null) return "Region summary; select a squad for order detail";
		return "Strategic planet summary";
	}

	private IReadOnlyList<DossierCardData> BuildContextCards()
	{
		if (_selectedPlanet == null) return Array.Empty<DossierCardData>();
		if (_regionContextFocused && _selectedRegion != null) return BuildRegionCards(_selectedRegion);
		if (_selectedLoadedSquad != null) return BuildSquadCards(_selectedLoadedSquad);
		if (_selectedLandedSquad != null) return BuildSquadCards(_selectedLandedSquad);
		if (_selectedShip != null) return BuildShipCards(_selectedShip);
		if (_selectedRegion != null) return BuildRegionCards(_selectedRegion);
		return BuildPlanetCards(_selectedPlanet);
	}

	private IReadOnlyList<DossierCardData> BuildPlanetCards(Planet planet)
	{
		List<DossierCardData> cards = [];
		Faction controllingFaction = planet.GetControllingFaction();
		bool imperialOrPlayer = FactionRelationshipService.IsImperial(controllingFaction);

		List<ValueTuple<string, string>> worldRows = [Row("Control", controllingFaction?.Name ?? "Unknown")];
		if (imperialOrPlayer)
		{
			worldRows.Add(Row("Classification", planet.Template.Name));
			worldRows.Add(Row("Population", planet.Population.ToString("N0")));
			worldRows.Add(Row("PDF Size", planet.PlanetaryDefenseForces.ToString("N0")));
			worldRows.Add(Row("Aestimare", ConvertImportanceToString(planet.Importance)));
			worldRows.Add(Row("Tithe Grade", ConvertTaxRangeToString(planet.TaxLevel)));
		}
		else if (controllingFaction != null)
		{
			worldRows.Add(Row("Xenos Present", controllingFaction.Name));
		}
		worldRows.Add(Row("Regions", planet.Regions.Length.ToString()));
		worldRows.Add(Row("Orbiting Task Forces", planet.OrbitingTaskForceList.Count.ToString()));
		cards.Add(new DossierCardData("World", planet.Name, worldRows, OnlyWarStyle.Gold));

		int overrideCount = planet.LoadoutDoctrine.Loadouts.Count;
		int doctrineFollowers = GetPlanetSquads(planet).Count(squad => squad.UsesLoadoutDoctrine);
		int customSquads = GetPlanetSquads(planet).Count(squad => !squad.UsesLoadoutDoctrine);
		cards.Add(new DossierCardData(
			"Theater Doctrine",
			overrideCount == 0 ? "Inherits Chapter" : $"{overrideCount} override{(overrideCount == 1 ? "" : "s")}",
			[
				Row("Following Doctrine", doctrineFollowers.ToString()),
				Row("Custom Squads", customSquads.ToString()),
				Row("Edit", "Theater Loadouts in header")
			],
			OnlyWarStyle.PlayerAccent));

		if (imperialOrPlayer)
		{
			Character governor = planet.PlanetFactionMap[controllingFaction.Id].Leader;
			if (governor != null)
			{
				List<ValueTuple<string, string>> governorRows =
				[
					Row("Opinion", ConvertOpinionToString(governor.OpinionOfPlayerForce)),
					Row("Civil Assessment", GovernorCivilAssessmentService.Assess(planet, governor)),
					Row("Active Request", governor.ActiveRequest != null ? "Yes" : "No")
				];
				cards.Add(new DossierCardData("Governor", governor.Name, governorRows, OnlyWarStyle.PlayerAccent));

				// The bare "Active Request: Yes" above used to be the whole story on this screen:
				// it announced a petition existed and gave the player no way to learn its terms,
				// its deadline, or how to answer it. The detail lives on the Diplomacy screen, but
				// nothing pointed there, so the request read as a dead end (issue #3).
				if (governor.ActiveRequest != null)
				{
					cards.Add(BuildGovernorRequestCard(planet, governor.ActiveRequest));
				}
			}
		}

		return cards;
	}

	private static List<Squad> GetPlanetSquads(Planet planet)
	{
		return planet.Regions
			.Where(region => region != null)
			.SelectMany(region => region.RegionFactionMap.Values)
			.SelectMany(regionFaction => regionFaction.LandedSquads)
			.Concat(planet.OrbitingTaskForceList.SelectMany(fleet => fleet.Ships)
				.SelectMany(ship => ship.LoadedSquads))
			.Where(squad => squad.Faction?.IsPlayerFaction == true)
			.GroupBy(squad => squad.Id)
			.Select(group => group.First())
			.ToList();
	}

	private static DossierCardData BuildGovernorRequestCard(Planet planet, IRequest request)
	{
		string commitment =
			$"{request.Commitment.PackageCount} "
			+ $"{request.Commitment.DisplayUnitName}"
			+ (request.Commitment.PackageCount == 1 ? "" : "s")
			+ $" / {request.Commitment.ServiceWeeks} wks";
		List<ValueTuple<string, string>> rows =
		[
			Row("Concern", request.ThreatFaction != null
				? $"{request.ThreatFaction.Name} in revolt"
				: "Unverified threat"),
			Row("Commitment", commitment),
			Row("Deadline", FormatRequestDate(request.Deadline)),
			Row("Progress", FormatRequestProgress(planet, request)),
			Row("Offer", request.OfferedScheduleKind == PledgeScheduleKind.Standing
				? $"{request.OfferedRequisition:N0} Req / {request.OfferedCadenceWeeks} wks"
				: $"{request.OfferedRequisition:N0} Req (one-off)"),
			Row("Full Terms", "Diplomacy screen")
		];
		return new DossierCardData(
			"Governor's Request", request.Requester?.Name ?? "Unknown", rows, OnlyWarStyle.Gold);
	}

	// Threat-suppression requests have no squad-week meter to report; effort requests do, and the
	// player also needs telling WHERE the order has to be held, since only the capital region counts.
	private static string FormatRequestProgress(Planet planet, IRequest request)
	{
		if (request.FulfillmentKind == RequestFulfillmentKind.ThreatSuppressed)
		{
			return "Suppress the threat";
		}

		decimal required = request.Commitment.PackageCount * request.Commitment.ServiceWeeks;
		decimal packageWeeks = request.Commitment.ReferenceBattleValuePerPackage <= 0
			? 0
			: (decimal)request.ProgressBattleValueTime
				/ request.Commitment.ReferenceBattleValuePerPackage;
		Region capital = planet.Regions.FirstOrDefault(r => r.Id == planet.CapitalRegionId)
			?? planet.Regions.FirstOrDefault();
		string where = capital == null ? "" : $" (Show of Force in {capital.Name})";
		return $"{packageWeeks:0.#}/{required:0.#} squad-wks{where}";
	}

	private static string FormatRequestDate(Date date) =>
		date == null ? "Unknown" : $"{date.Year:000}.M{date.Millenium} (wk {date.Week})";

	// Mirrors the Region Ops dossier ordering: region summary first, then the local Imperial
	// force, then hostile faction(s) - so the same data reads consistently across both screens.
	private IReadOnlyList<DossierCardData> BuildRegionCards(Region region)
	{
		List<DossierCardData> cards = [];
		float visibleIntel = region.GetPlayerVisibleIntel();
		RegionFaction playerRegionFaction = GetPlayerRegionFaction(region);
		List<RegionFaction> enemyFactions = GetPublicEnemyRegionFactions(region);

		List<ValueTuple<string, string>> regionRows =
		[
			Row("Control", GetRegionControlLabel(region)),
			Row("Intel Rating", $"{visibleIntel:0.##}")
		];
		if (region.HasHiddenDefaultFaction())
		{
			regionRows.Add(Row("Civilians", "Unknown"));
		}
		else
		{
			long civilianPopulation = region.GetVisibleCivilianPopulation();
			regionRows.Add(Row("Civilians", civilianPopulation > 0 ? civilianPopulation.ToString("N0") : "None"));
		}
		cards.Add(new DossierCardData("Region", null, regionRows, OnlyWarStyle.Gold));

		List<ValueTuple<string, string>> localRows =
		[
			Row("Marines", playerRegionFaction?.LandedSquads.Sum(squad => squad.Members.Count).ToString() ?? "0"),
			Row("Assigned Orders", playerRegionFaction?.LandedSquads.Count(squad => squad.CurrentOrders != null).ToString() ?? "0"),
			Row("PDF Garrison", region.PlanetaryDefenseForces > 0 ? region.PlanetaryDefenseForces.ToString("N0") : "None")
		];
		// The Imperial position in this region - the Chapter's works and the PDF's pooled - so
		// fortifications the player ordered are visible here rather than only in the turn report.
		RegionFaction imperialFaction = playerRegionFaction
			?? region.RegionFactionMap.Values.FirstOrDefault(rf => rf.PlanetFaction.Faction.IsDefaultFaction);
		if (imperialFaction != null)
		{
			localRows.Add(Row("Entrenchment", DescribeOwnShared(imperialFaction, DefenseType.Entrenchment)));
			localRows.Add(Row("Listening Posts", DescribeOwnShared(imperialFaction, DefenseType.ListeningPost)));
			localRows.Add(Row("Anti-Air", DescribeOwnShared(imperialFaction, DefenseType.AntiAir)));
		}
		cards.Add(new DossierCardData("Local Force", "Imperial Presence", localRows, OnlyWarStyle.PlayerAccent));

		if (enemyFactions.Count > 0)
		{
			foreach (RegionFaction enemyFaction in enemyFactions)
			{
				List<ValueTuple<string, string>> enemyRows = [Row("Force Magnitude", enemyFaction.GetForceMagnitudeDescription())];
				if (visibleIntel > 1)
				{
					enemyRows.Add(Row("Entrenchment", DescribeShared(enemyFaction, DefenseType.Entrenchment)));
					enemyRows.Add(Row("Listening Posts", DescribeShared(enemyFaction, DefenseType.ListeningPost)));
					enemyRows.Add(Row("Anti-Air", DescribeShared(enemyFaction, DefenseType.AntiAir)));
				}
				cards.Add(new DossierCardData("Hostile Faction", enemyFaction.PlanetFaction.Faction.Name, enemyRows, OnlyWarStyle.OpposingAccent));
			}
		}
		else
		{
			cards.Add(new DossierCardData("Hostile Faction", "None Detected", Array.Empty<ValueTuple<string, string>>(), OnlyWarStyle.OpposingAccent));
		}

		// Mirrors the Region Ops dossier's Inbound Orders card: every player order aimed at this
		// region from anywhere in the sector, so recon/advance converging from a different region
		// is visible here too. Static (informational) to match this screen's card idiom - orders
		// are edited on the Region Ops screen ("Open Region").
		List<InboundOrderInfo> inbound = InboundOrders.ForRegion(region);
		List<ValueTuple<string, string>> inboundRows = inbound
			.Select(info => Row(
				$"{info.MissionAndAggressionLabel} · from {info.OriginLabel}",
				info.SquadCount == 1 ? "1 squad" : $"{info.SquadCount} squads"))
			.ToList();
		cards.Add(new DossierCardData(
			"Inbound Orders",
			inbound.Count == 0 ? "None" : null,
			inboundRows,
			OnlyWarStyle.PlayerAccent));

		return cards;
	}

	private static IReadOnlyList<DossierCardData> BuildSquadCards(Squad squad)
	{
		List<ValueTuple<string, string>> rows =
		[
			Row("Unit", squad.ParentUnit?.Name ?? "Unknown"),
			Row("Fighting Strength", $"{squad.Members.Count(member => member.IsCombatEffective)}/{squad.Members.Count}"),
			Row("Location", squad.BoardedLocation != null ? $"Aboard {squad.BoardedLocation.Name}" : squad.CurrentRegion?.Name ?? "Unknown"),
			Row("Orders", squad.CurrentOrders?.Mission.MissionType.ToString() ?? "Unassigned")
		];
		rows.Add(Row("Loadout", LoadoutDoctrineService.DescribeSource(
			LoadoutDoctrineService.Resolve(squad))));
		if (squad.CurrentOrders != null)
		{
			rows.Add(Row("Target Region", squad.CurrentOrders.Mission.RegionFaction.Region.Name));
			rows.Add(Row("Aggression", squad.CurrentOrders.LevelOfAggression.ToString()));
		}
		float? strengthBar = squad.Members.Count > 0
			? (float)squad.Members.Count(member => member.IsCombatEffective) / squad.Members.Count
			: null;
		return [new DossierCardData("Squad", squad.Name, rows, OnlyWarStyle.PlayerAccent, strengthBar)];
	}

	internal static IReadOnlyList<DossierCardData> BuildShipCards(Ship ship)
	{
		List<ValueTuple<string, string>> rows =
		[
			Row("Loaded", $"{ship.LoadedSoldierCount}/{ship.Template.SoldierCapacity}"),
			Row("Available Capacity", ship.AvailableCapacity.ToString()),
			Row("Loaded Squads", ship.LoadedSquads.Count.ToString())
		];
		return [new DossierCardData("Transport", ship.Name, rows, OnlyWarStyle.PlayerAccent)];
	}

	private IReadOnlyList<CommandAction> BuildCommands()
	{
		return
		[
			new(ActionOpenRegion, "Open Region", "map_pin", _selectedRegion != null),
			new(ActionOpenSquad, "Open Squad", "player_forces", GetSelectedSquad() != null),
			new(ActionLand, GetLandCommandText(), "land_squads", CanLand()),
			new(ActionLoad, GetLoadCommandText(), "load_squads", CanLoad())
		];
	}

	private IReadOnlyList<IReadOnlyList<CommandAction>> BuildCommandRows()
	{
		IReadOnlyList<CommandAction> commands = BuildCommands();
		return
		[
			[commands[0], commands[1]],
			[commands[2], commands[3]]
		];
	}

	private void ApplySelectionKey(string key)
	{
		if (!IsSurfaceRosterSelectionKey(key)) ClearSurfaceForceSelection();
		if (!IsOrbitalRosterSelectionKey(key)) ClearOrbitalSelection();
		if (string.IsNullOrWhiteSpace(key) || key.StartsWith("group:") || key.StartsWith("presence:"))
		{
			_regionContextFocused = true;
			return;
		}

		string[] parts = key.Split(':');
		switch (parts[0])
		{
			case "region":
				_selectedRegion = _selectedPlanet.Regions.FirstOrDefault(region => region.Id == int.Parse(parts[1]));
				_regionContextFocused = true;
				_selectedSurfaceRegion = _selectedRegion;
				_selectedLandedSquad = null;
				break;
			case "ship":
				_regionContextFocused = false;
				_selectedShip = FindShip(int.Parse(parts[1]));
				_selectedLoadedSquad = null;
				break;
			case "loaded-unit":
				_regionContextFocused = false;
				_selectedShip = FindShip(int.Parse(parts[1]));
				_selectedLoadedSquad = null;
				break;
			case "loaded-squad":
				_regionContextFocused = false;
				_selectedShip = FindShip(int.Parse(parts[1]));
				_selectedLoadedSquad = FindSquadById(_selectedShip?.LoadedSquads, int.Parse(parts[2]));
				if (_selectedLoadedSquad == null) _selectedShip = null;
				break;
			case "surface-unit":
				_selectedRegion = _selectedPlanet.Regions.FirstOrDefault(region => region.Id == int.Parse(parts[1]));
				_regionContextFocused = true;
				_selectedSurfaceRegion = _selectedRegion;
				_selectedLandedSquad = null;
				break;
			case "surface-squad":
				_selectedRegion = _selectedPlanet.Regions.FirstOrDefault(region => region.Id == int.Parse(parts[1]));
				_regionContextFocused = false;
				_selectedSurfaceRegion = _selectedRegion;
				_selectedLandedSquad = FindSquadById(
					GetPlayerRegionFaction(_selectedSurfaceRegion)?.LandedSquads,
					int.Parse(parts[2]));
				if (_selectedLandedSquad == null) _selectedSurfaceRegion = null;
				break;
		}
	}

	internal static Squad FindSquadById(IEnumerable<Squad> squads, int squadId)
	{
		return squads?.FirstOrDefault(squad => squad.Id == squadId);
	}

	internal static bool IsSurfaceRosterSelectionKey(string key)
	{
		return key?.StartsWith("region:") == true
			|| key?.StartsWith("surface-unit:") == true
			|| key?.StartsWith("surface-squad:") == true;
	}

	private static bool IsOrbitalRosterSelectionKey(string key)
	{
		return key?.StartsWith("ship:") == true
			|| key?.StartsWith("loaded-unit:") == true
			|| key?.StartsWith("loaded-squad:") == true;
	}

	private void LandSelectedForces()
	{
		// Multi-select notifications are deferred until Godot has settled the whole click
		// transaction, so re-read the Tree at commit time rather than trusting the cached sets.
		RecomputeSelectedSquads();
		if (!CanLand()) return;
		RegionFaction regionFaction = GetOrCreatePlayerRegionFaction(_selectedRegion);
		bool changed = false;

		foreach (Squad squad in _selectedLoadedSquads.ToList())
		{
			Ship ship = squad.BoardedLocation;
			ship?.RemoveSquad(squad);
			if (!regionFaction.LandedSquads.Contains(squad))
			{
				regionFaction.LandedSquads.Add(squad);
			}
			squad.CurrentRegion = _selectedRegion;
			squad.BoardedLocation = null;
			changed = true;
		}

		if (changed) CampaignChanged?.Invoke(this, EventArgs.Empty);
		ClearForceSelections();
		RefreshWorkspace();
	}

	private void ShowEmbarkShipMenu()
	{
		RecomputeSelectedSquads();
		List<Squad> squads = _selectedLandedSquads.ToList();
		if (squads.Count == 0) return;

		int capacityRequired = squads.Sum(squad => squad.Members.Count);
		List<Ship> ships = GetPlayerOrbitingShips().ToList();

		_embarkShipMenu.Clear();
		foreach (Ship ship in ships)
		{
			int itemIndex = _embarkShipMenu.ItemCount;
			string label = $"{ship.Name} — {ship.LoadedSoldierCount}/{ship.Template.SoldierCapacity} aboard";
			_embarkShipMenu.AddItem(label, ship.Id);

			bool hasCapacity = capacityRequired <= ship.AvailableCapacity;
			_embarkShipMenu.SetItemDisabled(itemIndex, !hasCapacity);
			_embarkShipMenu.SetItemTooltip(
				itemIndex,
				hasCapacity
					? $"Embark {squads.Count} squad{(squads.Count == 1 ? "" : "s")} ({capacityRequired} troops) on {ship.Name}."
					: $"{ship.Name} needs {capacityRequired} free spaces but has {ship.AvailableCapacity}.");
		}

		_embarkShipMenu.Position = (Vector2I)GetViewport().GetMousePosition();
		_embarkShipMenu.ResetSize();
		_embarkShipMenu.Popup();
	}

	private void OnEmbarkShipSelected(long shipId)
	{
		Ship ship = FindShip((int)shipId);
		if (ship == null) return;

		LoadSelectedForces(ship);
	}

	private void LoadSelectedForces(Ship destinationShip)
	{
		List<Squad> squads = _selectedLandedSquads.ToList();
		int capacityRequired = squads.Sum(squad => squad.Members.Count);
		if (destinationShip == null || squads.Count == 0 || capacityRequired > destinationShip.AvailableCapacity) return;

		bool changed = false;

		// A multi-row selection can pull squads out of several regions at once, so embark region by
		// region and give each vacated region its own cleanup pass.
		foreach (IGrouping<Region, Squad> group in squads
			.Where(squad => squad.CurrentRegion != null)
			.GroupBy(squad => squad.CurrentRegion))
		{
			RegionFaction regionFaction = GetPlayerRegionFaction(group.Key);
			if (regionFaction == null) continue;

			foreach (Squad squad in group)
			{
				regionFaction.LandedSquads.Remove(squad);
				destinationShip.LoadSquad(squad);
				squad.CurrentRegion = null;
				squad.BoardedLocation = destinationShip;
				changed = true;
			}

			CleanupPlayerRegionFactionAfterLoad(group.Key, regionFaction);
		}

		if (changed) CampaignChanged?.Invoke(this, EventArgs.Empty);
		ClearForceSelections();
		RefreshWorkspace();
	}

	private bool CanLand()
	{
		return _selectedRegion != null
			&& _selectedRegion.Planet != null
			&& GameDataSingleton.Instance?.Sector?.PlayerForce?.Faction != null
			&& _selectedLoadedSquads.Count > 0;
	}

	private bool CanLoad()
	{
		return _selectedLandedSquads.Sum(squad => squad.Members.Count) > 0 && GetPlayerOrbitingShips().Any();
	}

	private string GetLandCommandText()
	{
		if (_selectedRegion == null || _selectedLoadedSquads.Count == 0) return "Land Selected";
		return BuildLandCommandText(
			IsSingleRowSelection("loaded-squad:"), _selectedLoadedSquads.Count, _selectedRegion.Name);
	}

	private string GetLoadCommandText()
	{
		// A multi-row selection can span regions, so name the single origin when there is one and
		// fall back to a count when the force is being pulled out of several at once.
		List<Region> origins = GetSelectedLandedRegions();
		if (origins.Count == 0) return "Embark Selected";
		string originName = origins.Count == 1 ? origins[0].Name : $"{origins.Count} Regions";
		return BuildEmbarkCommandText(
			IsSingleRowSelection("surface-squad:"), _selectedLandedSquads.Count, originName);
	}

	private List<Region> GetSelectedLandedRegions()
	{
		return _selectedLandedSquads
			.Select(squad => squad.CurrentRegion)
			.Where(region => region != null)
			.Distinct()
			.ToList();
	}

	internal static string BuildLandCommandText(bool singleSquadSelected, int squadCount, string regionName)
	{
		if (singleSquadSelected) return $"Land Squad in {regionName}";
		return $"Land {squadCount} Squad{(squadCount == 1 ? "" : "s")} in {regionName}";
	}

	internal static string BuildEmbarkCommandText(bool singleSquadSelected, int squadCount, string regionName)
	{
		if (singleSquadSelected) return $"Embark Squad From {regionName}";
		return $"Embark {squadCount} Squad{(squadCount == 1 ? "" : "s")} From {regionName}";
	}

	// Rebuilds the orbital and surface squad sets from every selected roster row. Called before any
	// command text or commit, so the Tree stays the single source of truth for what a command acts
	// on even when Godot's deferred multi-select notification lags a frame behind the visible rows.
	private void RecomputeSelectedSquads()
	{
		_selectedRosterKeys = _view.GetSelectedKeys();
		_selectedLoadedSquads.Clear();
		_selectedLandedSquads.Clear();
		if (_selectedPlanet == null) return;

		foreach (string key in _selectedRosterKeys)
		{
			if (string.IsNullOrWhiteSpace(key)) continue;
			string[] parts = key.Split(':');
			if (parts.Length < 2 || !int.TryParse(parts[1], out int locationId)) continue;

			if (IsOrbitalRosterSelectionKey(key))
			{
				AddDistinct(_selectedLoadedSquads, ExpandRosterSelection(FindShip(locationId)?.LoadedSquads, key));
			}
			else if (IsSurfaceRosterSelectionKey(key))
			{
				Region region = _selectedPlanet.Regions.FirstOrDefault(candidate => candidate.Id == locationId);
				AddDistinct(_selectedLandedSquads, ExpandRosterSelection(GetPlayerRegionFaction(region)?.LandedSquads, key));
			}
		}
	}

	// Expands one roster row into the squads it commands: a squad row is itself, a unit row is every
	// operational squad of that unit in the location, and a ship or region row is the whole location.
	// Ctrl-clicking a unit therefore adds or removes all of its squads at once.
	internal static IEnumerable<Squad> ExpandRosterSelection(IEnumerable<Squad> locationSquads, string key)
	{
		if (locationSquads == null || string.IsNullOrWhiteSpace(key)) return [];
		string[] parts = key.Split(':');
		IEnumerable<Squad> operational = locationSquads.Where(squad => squad.IsOperational);
		return parts[0] switch
		{
			"ship" or "region" => operational,
			"loaded-unit" or "surface-unit" when parts.Length > 2 =>
				operational.Where(squad => (squad.ParentUnit?.Id ?? 0) == int.Parse(parts[2])),
			"loaded-squad" or "surface-squad" when parts.Length > 2 =>
				operational.Where(squad => squad.Id == int.Parse(parts[2])),
			_ => []
		};
	}

	private static void AddDistinct(List<Squad> target, IEnumerable<Squad> squads)
	{
		foreach (Squad squad in squads)
		{
			if (!target.Contains(squad)) target.Add(squad);
		}
	}

	// The multi-select notification names the row that was toggled, which on a ctrl-click
	// deselection is a row that just left the selection. Fall back to a surviving row so the
	// dossier follows what is still selected.
	private string ResolveContextKey(string key)
	{
		if (!string.IsNullOrEmpty(key) && _selectedRosterKeys.Contains(key)) return key;
		return _selectedRosterKeys.Count > 0 ? _selectedRosterKeys[^1] : "";
	}

	private bool IsSingleRowSelection(string keyPrefix)
	{
		return _selectedRosterKeys.Count == 1 && _selectedRosterKeys[0].StartsWith(keyPrefix);
	}

	private Squad GetSelectedSquad()
	{
		return _selectedLoadedSquad ?? _selectedLandedSquad;
	}

	private Ship FindShip(int shipId)
	{
		return _selectedPlanet?.OrbitingTaskForceList.SelectMany(taskForce => taskForce.Ships).FirstOrDefault(ship => ship.Id == shipId);
	}

	private IEnumerable<Ship> GetPlayerOrbitingShips()
	{
		Faction playerFaction = GameDataSingleton.Instance?.Sector?.PlayerForce?.Faction;
		if (_selectedPlanet == null || playerFaction == null) return [];

		return _selectedPlanet.OrbitingTaskForceList
			.Where(taskForce => taskForce.Faction == playerFaction)
			.SelectMany(taskForce => taskForce.Ships)
			.OrderBy(ship => ship.Name)
			.ThenBy(ship => ship.Id);
	}

	private void ClearOrbitalSelection()
	{
		_selectedShip = null;
		_selectedLoadedSquad = null;
	}

	private void ClearSurfaceForceSelection()
	{
		_selectedSurfaceRegion = null;
		_selectedLandedSquad = null;
	}

	private void ClearForceSelections()
	{
		ClearOrbitalSelection();
		ClearSurfaceForceSelection();
		// Drop the Tree's own selection too: PopulateSelectionTree restores selections by key, so a
		// ship or region row left selected here would silently re-arm the whole location after a
		// commit that only moved part of it.
		_view.ClearSelection();
		_selectedRosterKeys = [];
		_selectedLoadedSquads.Clear();
		_selectedLandedSquads.Clear();
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

		// No squads left here, so hand any works to an ally that can still man them - the same rule
		// the turn loop applies (PlanetTurnProcessor.TransferAbandonedWorksToAllies). Doing it here
		// too keeps a save loaded mid-campaign consistent with one that played through the turn.
		RegionDefenses.TransferToAlly(regionFaction);

		if (!RegionDefenses.HasAnyWorks(regionFaction))
		{
			region.RegionFactionMap.Remove(regionFaction.PlanetFaction.Faction.Id);
			return;
		}

		regionFaction.IsPublic = false;
	}

	private static string DescribeShared(RegionFaction regionFaction, DefenseType defenseType) =>
		RegionFactionExtensions.GetDefenseLevelDescription(
			RegionDefenses.GetShared(regionFaction, defenseType));

	// The player's own position carries the exact level too, so the turn report's projection can be
	// reconciled against the region rather than hidden inside a bucket. Enemy rows stay fuzzy.
	private static string DescribeOwnShared(RegionFaction regionFaction, DefenseType defenseType)
	{
		double level = RegionDefenses.GetShared(regionFaction, defenseType);
		string rating = RegionFactionExtensions.GetDefenseLevelDescription(level);
		return level > 0 ? $"{rating} ({level:F2})" : rating;
	}

	private static List<RegionFaction> GetPublicEnemyRegionFactions(Region region)
	{
		if (region?.Planet?.RelationshipLedger == null) return [];
		FactionIntelligenceService.ObservePublicActivity(region.Planet, 0);
		return IntelligenceTargetService.GetPlayerVisibleTargets(region)
			.Select(target => target.CurrentPresence)
			.Where(rf => rf != null && rf.IsPublic)
			.ToList();
	}

	private static string GetRegionControlLabel(Region region)
	{
		return region.ControllingFaction?.PlanetFaction.Faction.Name ?? "Contested";
	}

	private static ValueTuple<string, string> Row(string label, string value)
	{
		return new ValueTuple<string, string>(label, value);
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
