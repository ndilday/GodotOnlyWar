using System;
using System.Collections.Generic;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Command;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.Storage;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Runtime.Allocators;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Events;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Application;

/// <summary>
/// Public UI facade for one campaign host. Screen behavior lives in the concrete screen services;
/// this type only composes them, owns session lifetime, and forwards the existing UI contracts.
/// </summary>
public sealed class CampaignApplication :
    ICampaignNavigationApplication,
    IChapterScreenApplication,
    ICommandScreenApplication,
    IDiplomacyScreenApplication,
    IFleetScreenApplication,
    ILoadoutScreenApplication,
    IMainScreenApplication,
    IMedicalScreenApplication,
    IMusterScreenApplication,
    IOperationsScreenApplication,
    ISectorMapApplication,
    ISessionControlApplication,
    ISystemInspectorApplication,
    ITrainingScreenApplication
{
    private readonly CampaignApplicationContext _context;
    private readonly CampaignNavigationApplication _navigation;
    private readonly ChapterScreenApplication _chapter;
    private readonly CommandScreenApplication _command;
    private readonly DiplomacyScreenApplication _diplomacy;
    private readonly FleetScreenApplication _fleet;
    private readonly LoadoutScreenApplication _loadout;
    private readonly MainScreenApplication _main;
    private readonly MedicalScreenApplication _medical;
    private readonly MusterScreenApplication _muster;
    private readonly OperationsScreenApplication _operations;
    private readonly SectorMapApplication _sectorMap;
    private readonly SessionControlApplication _sessionControl;
    private readonly SystemInspectorApplication _systemInspector;
    private readonly TrainingScreenApplication _training;

    public CampaignApplication(CampaignServices services)
    {
        _context = new CampaignApplicationContext(services);
        _operations = new OperationsScreenApplication(_context);
        _navigation = new CampaignNavigationApplication(_context);
        _chapter = new ChapterScreenApplication(_context);
        _command = new CommandScreenApplication(_context);
        _diplomacy = new DiplomacyScreenApplication(_context);
        _fleet = new FleetScreenApplication(_context);
        _loadout = new LoadoutScreenApplication(_context);
        _main = new MainScreenApplication(_context);
        _medical = new MedicalScreenApplication(_context);
        _muster = new MusterScreenApplication(_context);
        _sectorMap = new SectorMapApplication(_context);
        _sessionControl = new SessionControlApplication(_context);
        _systemInspector = new SystemInspectorApplication(
            _context, _operations.Queries, _fleet);
        _training = new TrainingScreenApplication(_context);
    }

    // The public facade deliberately publishes screen contracts and lifecycle commands, not the
    // live session aggregate or the complete service graph. Tests and application-internal
    // composition retain friend/internal access while host code uses these narrow persistence
    // capabilities where it genuinely needs them.
    internal CampaignServices Services => _context.Services;
    internal GameSession ActiveSession => _context.ActiveSession;
    public GameStorage Storage => _context.Services.Persistence.Storage;
    public SaveGameManager SaveManager => _context.Services.Persistence.SaveManager;
    public Guid SessionToken => _context.SessionToken;

    public event EventHandler SessionChanged
    {
        add => _context.SessionChanged += value;
        remove => _context.SessionChanged -= value;
    }

    public event EventHandler CampaignStatusChanged
    {
        add => _sessionControl.CampaignStatusChanged += value;
        remove => _sessionControl.CampaignStatusChanged -= value;
    }

    // These properties are useful to alternate hosts that want one screen contract without the
    // aggregate facade. The ordinary Godot host continues to inject CampaignApplication itself.
    public ICampaignNavigationApplication Navigation => _navigation;
    public IChapterScreenApplication ChapterScreen => _chapter;
    public ICommandScreenApplication CommandScreen => _command;
    public IDiplomacyScreenApplication DiplomacyScreen => _diplomacy;
    public IFleetScreenApplication FleetScreen => _fleet;
    public ILoadoutScreenApplication LoadoutScreen => _loadout;
    public IMainScreenApplication MainScreen => _main;
    public IMedicalScreenApplication MedicalScreen => _medical;
    public IMusterScreenApplication MusterScreen => _muster;
    public IOperationsScreenApplication OperationsScreen => _operations;
    public ISectorMapApplication SectorMap => _sectorMap;
    public ISessionControlApplication SessionControl => _sessionControl;
    public ISystemInspectorApplication SystemInspector => _systemInspector;
    public ITrainingScreenApplication TrainingScreen => _training;

    public GameSession CreateNewCampaign(
        GameRulesData rules,
        Date date,
        string chapterName = null,
        int seed = 1,
        ScenarioFactionSelection invaderSelection = null)
    {
        if (rules == null) throw new ArgumentNullException(nameof(rules));
        if (date == null) throw new ArgumentNullException(nameof(date));

        PersistentIdAllocator identity = new();
        Sector candidate = SectorBuilder.GenerateSector(
            seed,
            rules,
            date,
            Services.Generation.CreateSupport(rules, date, Services.Random, identity),
            chapterName,
            invaderSelection);
        return new GameSession(rules, candidate, date, Services.Random, identity);
    }

    public GameSession LoadCampaign(string savePath) =>
        Services.Persistence.CampaignLoader.LoadSession(
            savePath,
            Services.Random,
            Services.Operations.Commitments);

    public GameSession StartNewCampaign(
        GameRulesData rules,
        Date date,
        string chapterName = null,
        int seed = 1,
        ScenarioFactionSelection invaderSelection = null)
    {
        GameSession session = CreateNewCampaign(
            rules, date, chapterName, seed, invaderSelection);
        Install(session);
        return session;
    }

    public GameSession LoadAndInstall(string savePath)
    {
        GameSession session = LoadCampaign(savePath);
        Install(session);
        return session;
    }

    public void Install(GameSession session) => _context.Install(session);

    public TurnResolutionResult AdvanceTurn(GameSession session = null) =>
        _context.AdvanceTurn(session);

    public void Save(string filePath, GameSession session = null) =>
        _context.Save(filePath, session);

    public void Close() => _context.Close();

    // ICampaignNavigationApplication
    public CampaignNavigationRoute ResolveNavigation(
        CampaignNavigationTargetKind kind, int? primaryId) =>
        _navigation.ResolveNavigation(kind, primaryId);

    public CampaignNavigationRoute ResolveSquadLocation(int squadId) =>
        _navigation.ResolveSquadLocation(squadId);

    public int? QueryRegionPlanet(int regionId) => _navigation.QueryRegionPlanet(regionId);
    public string QueryPlanetName(int planetId) => _navigation.QueryPlanetName(planetId);

    // IChapterScreenApplication
    public bool HasChapter => _chapter.HasChapter;
    public ChapterBrowserView QueryChapterBrowser(ChapterBrowserQuery query) =>
        _chapter.QueryChapterBrowser(query);
    public ChapterFilterOptions QueryFilterOptions(ChapterBrowserQuery query) =>
        _chapter.QueryFilterOptions(query);
    public int? FindCompanyForSquad(int squadId) => _chapter.FindCompanyForSquad(squadId);
    public bool CanNavigateToSquad(int squadId) => _chapter.CanNavigateToSquad(squadId);
    public ChapterPrompt DescribeTransfer(int soldierId, int optionIndex) =>
        _chapter.DescribeTransfer(soldierId, optionIndex);
    public ChapterTransferResult ConfirmTransfer(
        Guid sessionToken, int soldierId, int optionIndex, IReadOnlyList<int> contextSoldierIds) =>
        _chapter.ConfirmTransfer(sessionToken, soldierId, optionIndex, contextSoldierIds);
    public ChapterPrompt DescribeRecall(int soldierId) => _chapter.DescribeRecall(soldierId);
    public ChapterTransferResult ConfirmRecall(Guid sessionToken, int soldierId) =>
        _chapter.ConfirmRecall(sessionToken, soldierId);

    // ICommandScreenApplication
    public bool HasCampaign => _command.HasCampaign;
    public bool HasLastTurnReport => _command.HasLastTurnReport;
    public CommandBriefModel QueryBrief() => _command.QueryBrief();
    public ChronicleView QueryChronicle(ChronicleFilter filter, int page) =>
        _command.QueryChronicle(filter, page);

    // IDiplomacyScreenApplication
    public DiplomacyBoardView QueryDiplomacy() => _diplomacy.QueryDiplomacy();

    // IFleetScreenApplication
    public FleetRosterView QueryFleetScreen() => _fleet.QueryFleetScreen();
    public bool CanTransferSquadToShip(int squadId, int shipId) =>
        _fleet.CanTransferSquadToShip(squadId, shipId);
    public bool CanTransferUnitToShip(int unitId, int sourceShipId, int destinationShipId) =>
        _fleet.CanTransferUnitToShip(unitId, sourceShipId, destinationShipId);
    public FleetCommandResult TransferSquadToShip(Guid sessionToken, int squadId, int shipId) =>
        _fleet.TransferSquadToShip(sessionToken, squadId, shipId);
    public FleetCommandResult TransferUnitToShip(
        Guid sessionToken, int unitId, int sourceShipId, int destinationShipId) =>
        _fleet.TransferUnitToShip(sessionToken, unitId, sourceShipId, destinationShipId);
    public FleetActionAvailability QueryFleetActions(int fleetId) =>
        _fleet.QueryFleetActions(fleetId);
    public FleetLocationView QueryFleetLocation(int fleetId) =>
        _fleet.QueryFleetLocation(fleetId);
    public FleetMoveOptionsView QueryFleetMoveOptions(int fleetId) =>
        _fleet.QueryFleetMoveOptions(fleetId);
    public FleetRouteView QueryFleetRoute(int fleetId, int destinationPlanetId) =>
        _fleet.QueryFleetRoute(fleetId, destinationPlanetId);
    public FleetCommandResult PlotCourse(Guid sessionToken, int fleetId, int destinationPlanetId) =>
        _fleet.PlotCourse(sessionToken, fleetId, destinationPlanetId);
    public FleetDivideOptionsView QueryFleetDivideOptions(int fleetId) =>
        _fleet.QueryFleetDivideOptions(fleetId);
    public FleetDivideSelectionView EvaluateDivideSelection(
        int fleetId, IReadOnlyList<int> shipIds) => _fleet.EvaluateDivideSelection(fleetId, shipIds);
    public FleetCommandResult DivideFleet(
        Guid sessionToken, int fleetId, IReadOnlyList<int> shipIds) =>
        _fleet.DivideFleet(sessionToken, fleetId, shipIds);
    public FleetMergeOptionsView QueryFleetMergeOptions(int fleetId) =>
        _fleet.QueryFleetMergeOptions(fleetId);
    public FleetMergeSelectionView EvaluateMergeSelection(int fleetId, int targetFleetId) =>
        _fleet.EvaluateMergeSelection(fleetId, targetFleetId);
    public FleetCommandResult MergeFleet(Guid sessionToken, int fleetId, int targetFleetId) =>
        _fleet.MergeFleet(sessionToken, fleetId, targetFleetId);

    // ILoadoutScreenApplication
    public SquadLoadoutView QuerySquadLoadout(int squadId) => _loadout.QuerySquadLoadout(squadId);
    public LoadoutCommandResult SetSquadLoadout(
        Guid sessionToken, int squadId, IReadOnlyList<WeaponSet> loadout) =>
        _loadout.SetSquadLoadout(sessionToken, squadId, loadout);
    public LoadoutCommandResult ReturnSquadToDoctrine(Guid sessionToken, int squadId) =>
        _loadout.ReturnSquadToDoctrine(sessionToken, squadId);
    public LoadoutCommandResult SetSquadCharacterWeaponSet(
        Guid sessionToken, int squadId, int soldierId, WeaponSet weaponSet) =>
        _loadout.SetSquadCharacterWeaponSet(sessionToken, squadId, soldierId, weaponSet);
    public LoadoutCommandResult ResetSquadCharacterLoadout(
        Guid sessionToken, int squadId, int soldierId) =>
        _loadout.ResetSquadCharacterLoadout(sessionToken, squadId, soldierId);
    public EquipmentEditorView QuerySoldierEquipmentEditor(int squadId, int soldierId) =>
        _loadout.QuerySoldierEquipmentEditor(squadId, soldierId);
    public LoadoutCommandResult SaveSoldierEquipment(
        Guid sessionToken, int squadId, int soldierId, EquipmentLoadout loadout) =>
        _loadout.SaveSoldierEquipment(sessionToken, squadId, soldierId, loadout);
    public LoadoutDoctrineScopeView QueryDoctrineScope(int? planetId) =>
        _loadout.QueryDoctrineScope(planetId);
    public LoadoutTemplateDetailView QueryTemplateLoadout(int? planetId, int templateId) =>
        _loadout.QueryTemplateLoadout(planetId, templateId);
    public LoadoutCommandResult SaveTemplateLoadout(
        Guid sessionToken, int? planetId, int templateId, IReadOnlyList<WeaponSet> loadout) =>
        _loadout.SaveTemplateLoadout(sessionToken, planetId, templateId, loadout);
    public LoadoutCommandResult InheritTemplateLoadout(
        Guid sessionToken, int planetId, int templateId) =>
        _loadout.InheritTemplateLoadout(sessionToken, planetId, templateId);
    public IReadOnlyList<CharacterLoadoutRowData> QueryCharacterRoles() =>
        _loadout.QueryCharacterRoles();
    public LoadoutCommandResult SetCharacterRoleWeaponSet(
        Guid sessionToken, int roleId, WeaponSet weaponSet) =>
        _loadout.SetCharacterRoleWeaponSet(sessionToken, roleId, weaponSet);
    public LoadoutCommandResult ResetCharacterRole(Guid sessionToken, int roleId) =>
        _loadout.ResetCharacterRole(sessionToken, roleId);
    public EquipmentEditorView QueryRoleEquipmentEditor(int roleId) =>
        _loadout.QueryRoleEquipmentEditor(roleId);
    public LoadoutCommandResult SaveRoleEquipment(
        Guid sessionToken, int roleId, EquipmentLoadout loadout) =>
        _loadout.SaveRoleEquipment(sessionToken, roleId, loadout);
    public OperationalDoctrineView QueryOperationalDoctrine() =>
        _loadout.QueryOperationalDoctrine();
    public string DescribeOperationalDoctrineConsequence(
        int injuryThresholdIndex, bool requireDutyReadySquadLeader, int minimumStrength) =>
        _loadout.DescribeOperationalDoctrineConsequence(
            injuryThresholdIndex, requireDutyReadySquadLeader, minimumStrength);
    public LoadoutCommandResult SaveOperationalDoctrine(
        Guid sessionToken, int injuryThresholdIndex,
        bool requireDutyReadySquadLeader, int minimumStrength) =>
        _loadout.SaveOperationalDoctrine(
            sessionToken, injuryThresholdIndex, requireDutyReadySquadLeader,
            minimumStrength);

    // IMainScreenApplication
    public CampaignHeaderView QueryHeader() => _main.QueryHeader();
    public MainScreenStartupView QueryStartup() => _main.QueryStartup();
    public void AcknowledgeOpeningBrief(Guid sessionToken) =>
        _main.AcknowledgeOpeningBrief(sessionToken);
    public TurnReportView QueryLastTurnReport() => _main.QueryLastTurnReport();
    public ResolveTurnView ResolveTurn(Guid sessionToken) => _main.ResolveTurn(sessionToken);
    public NeophytePlacementOptions QueryNeophytePlacementTargets() =>
        _main.QueryNeophytePlacementTargets();
    public NeophytePlacementResult PlaceNeophyte(
        Guid sessionToken, int aspirantId, int squadId) =>
        _main.PlaceNeophyte(sessionToken, aspirantId, squadId);

    // IMedicalScreenApplication
    public MedicalScreenView QueryMedical(MedicalScreenQuery query) => _medical.QueryMedical(query);
    public RecoveryScreenView QueryRecovery(RecoveryQuery query) => _medical.QueryRecovery(query);
    public RecoveryPlanCommitResult ConfirmRecovery(ConfirmRecoveryCommand command) =>
        _medical.ConfirmRecovery(command);

    // IMusterScreenApplication
    public bool IsStaged(int soldierId) => _muster.IsStaged(soldierId);
    public int StagedActionCount => _muster.StagedActionCount;
    public IReadOnlyList<MusterScopeOption> QueryMusterScopes() => _muster.QueryMusterScopes();
    public ChapterFilterOptions QueryMusterFilterOptions(int? scopeCompanyId) =>
        _muster.QueryMusterFilterOptions(scopeCompanyId);
    public IReadOnlyList<MusterCandidateViewModel> QueryMusterCandidates(
        int? scopeCompanyId, MusterPopulationMode mode,
        IReadOnlyList<SoldierFilterCondition> filters) =>
        _muster.QueryMusterCandidates(scopeCompanyId, mode, filters);
    public IReadOnlyList<MusterFormationRow> QueryMusterFormations(int soldierId) =>
        _muster.QueryMusterFormations(soldierId);
    public MusterPreviewView QueryMusterPreview(int? soldierId, string formationSelectionKey) =>
        _muster.QueryMusterPreview(soldierId, formationSelectionKey);
    public MusterPlanView QueryMusterPlan() => _muster.QueryMusterPlan();
    public string DescribeStagedAction(int soldierId) => _muster.DescribeStagedAction(soldierId);
    public Guid? StageMusterAction(
        Guid sessionToken, int soldierId, string formationSelectionKey) =>
        _muster.StageMusterAction(sessionToken, soldierId, formationSelectionKey);
    public bool UndoMusterAction(Guid sessionToken, Guid actionId) =>
        _muster.UndoMusterAction(sessionToken, actionId);
    public bool UndoLastMusterAction(Guid sessionToken) => _muster.UndoLastMusterAction(sessionToken);
    public void ClearMusterPlan(Guid sessionToken) => _muster.ClearMusterPlan(sessionToken);
    public MusterCommitResultView CommitMusterPlan(Guid sessionToken) =>
        _muster.CommitMusterPlan(sessionToken);

    // IOperationsScreenApplication
    public OperationsWorkspaceView QueryOperations(OperationsWorkspaceQuery query) =>
        _operations.QueryOperations(query);
    public WorldDossierView QueryWorldDossier(int planetId, int regionId) =>
        _operations.QueryWorldDossier(planetId, regionId);
    public IReadOnlyList<DossierCardView> QueryRegionCards(int regionId) =>
        _operations.QueryRegionCards(regionId);
    public OperationsEntryView QueryEntry(int planetId, int? regionId) =>
        _operations.QueryEntry(planetId, regionId);
    public OperationsEntryView QueryGovernorRequestEntry(int planetId) =>
        _operations.QueryGovernorRequestEntry(planetId);
    public int? FindPlanetSquad(int planetId, int squadId) =>
        _operations.FindPlanetSquad(planetId, squadId);
    public OperationsSelection ResolveForceSelection(OperationsWorkspaceQuery query, string key) =>
        _operations.ResolveForceSelection(query, key);
    public int? FindOrderForMission(int regionId, string missionKey) =>
        _operations.FindOrderForMission(regionId, missionKey);
    public OperationsCommandResult SetOrderParticipants(OrderParticipantsCommand command) =>
        _operations.SetOrderParticipants(command);
    public OperationsCommandResult RemoveOrderSquad(RemoveOrderSquadCommand command) =>
        _operations.RemoveOrderSquad(command);
    public OrderCancellationPrompt DescribeOrderCancellation(int orderId) =>
        _operations.DescribeOrderCancellation(orderId);
    public OperationsCommandResult CancelOrder(CancelOrderCommand command) =>
        _operations.CancelOrder(command);
    public OperationsCommandResult SetOrderAggression(SetOrderAggressionCommand command) =>
        _operations.SetOrderAggression(command);
    public OperationsCommandResult ToggleOrderSpecialist(ToggleOrderSpecialistCommand command) =>
        _operations.ToggleOrderSpecialist(command);
    public OperationsCommandResult UndoLastOperation(UndoOperationsCommand command) =>
        _operations.UndoLastOperation(command);
    public OperationsCommandResult LandForce(LandForceCommand command) =>
        _operations.LandForce(command);
    public OperationsCommandResult EmbarkForce(EmbarkForceCommand command) =>
        _operations.EmbarkForce(command);
    public OperationsCommandResult DetachCasualties(DetachCasualtiesCommand command) =>
        _operations.DetachCasualties(command);

    // ISectorMapApplication / ISystemInspectorApplication
    public bool TryQuerySectorGrid(out int width, out int height) =>
        _sectorMap.TryQuerySectorGrid(out width, out height);
    public SectorMapGeometryView QuerySectorMapGeometry(bool useVoronoiBorders) =>
        _sectorMap.QuerySectorMapGeometry(useVoronoiBorders);
    public IReadOnlyList<SectorMapFleetMarker> QuerySectorMapFleets() =>
        _sectorMap.QuerySectorMapFleets();
    public IReadOnlyList<SectorMapPlanetLabelFacts> QuerySectorMapPlanetLabels() =>
        _sectorMap.QuerySectorMapPlanetLabels();
    public SectorMapSelectionView QuerySectorMapSelection(int planetId) =>
        _sectorMap.QuerySectorMapSelection(planetId);
    public SystemInspectorView QuerySystemInspector(
        int? planetId, int? selectedFleetId, bool includeDossier) =>
        _systemInspector.QuerySystemInspector(planetId, selectedFleetId, includeDossier);
    public int? QueryFleetContextPlanet(int fleetId) =>
        _systemInspector.QueryFleetContextPlanet(fleetId);

    // ISessionControlApplication
    public CampaignStatusView QueryStatus() => _sessionControl.QueryStatus();
    public void MarkChanged() => _sessionControl.MarkChanged();
    public SaveCampaignResult SaveCampaign(SaveCampaignCommand command) =>
        _sessionControl.SaveCampaign(command);
    public void WriteDiagnosticCapture(string filePath) =>
        _sessionControl.WriteDiagnosticCapture(filePath);
    public bool RequiresRecruitmentSetup() => _sessionControl.RequiresRecruitmentSetup();
    public EndTurnPreflightReport QueryEndTurnPreflight(
        EndTurnWarningPreferences preferences) => _sessionControl.QueryEndTurnPreflight(preferences);

    // ITrainingScreenApplication
    public RecruitmentDoctrineDraft QueryDoctrineDraft() => _training.QueryDoctrineDraft();
    public bool IsRecruitmentSetupComplete => _training.IsRecruitmentSetupComplete;
    public RecruitmentScreenSnapshot QueryRecruitmentScreen(
        RecruitmentDoctrineDraft draft, int? selectedSquadId) =>
        _training.QueryRecruitmentScreen(draft, selectedSquadId);
    public RecruitmentForecastView PreviewForecast(RecruitmentDoctrineDraft draft) =>
        _training.PreviewForecast(draft);
    public IReadOnlyList<ScoutSquadRow> QueryScoutSquads(int? selectedSquadId) =>
        _training.QueryScoutSquads(selectedSquadId);
    public TrainingCommandResult ConfirmDoctrine(
        Guid sessionToken, RecruitmentDoctrineDraft draft) =>
        _training.ConfirmDoctrine(sessionToken, draft);
    public TrainingCommandResult SetScoutTrainingOption(
        Guid sessionToken, int squadId, string optionKey) =>
        _training.SetScoutTrainingOption(sessionToken, squadId, optionKey);

    // Compatibility helpers retained for existing headless UI tests. They are forwarding shims,
    // not campaign behavior; new callers should use the owning screen service or its contract.
    internal static IEnumerable<ISoldier> OrderFilteredSoldiers(IEnumerable<ISoldier> soldiers) =>
        ChapterScreenApplication.OrderFilteredSoldiers(soldiers);

    internal static IEnumerable<Squad> OrderSquads(IEnumerable<Squad> squads) =>
        ChapterScreenApplication.OrderSquads(squads);

    internal static bool IsTrainingSquad(Squad squad) =>
        TrainingScreenApplication.IsTrainingSquad(squad);

    internal static string DescribeSquadListLabel(Squad squad) =>
        TrainingScreenApplication.DescribeSquadListLabel(squad);

    internal static string DescribeSquadListLabel(
        Squad squad, ScoutTrainingOptionCatalog trainingOptions) =>
        TrainingScreenApplication.DescribeSquadListLabel(squad, trainingOptions);

    internal static IEnumerable<Squad> OrderScoutSquads(IEnumerable<Squad> squads) =>
        TrainingScreenApplication.OrderScoutSquads(squads);

    internal static bool IsDoctrineValid(RecruitmentDoctrineDraft doctrine) =>
        TrainingScreenApplication.IsDoctrineValid(doctrine);

    internal static long GetChapterPopulation(Planet planet, int chapterFactionId) =>
        TrainingScreenApplication.GetChapterPopulation(planet, chapterFactionId);
}
