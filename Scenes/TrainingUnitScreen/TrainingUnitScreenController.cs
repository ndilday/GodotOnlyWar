using Godot;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

public sealed record RecruitmentStaffSummary(
    int ScoutSergeants,
    int Apothecaries,
    int Chaplains)
{
    public bool IsComplete =>
        ScoutSergeants > 0 && Apothecaries > 0 && Chaplains > 0;
}

public sealed record RecruitmentDoctrineDraft(
    RecruitmentPolicy Policy,
    int StrengthHalfSigmaSteps,
    int ConstitutionHalfSigmaSteps,
    int IntelligenceHalfSigmaSteps,
    int DexterityHalfSigmaSteps,
    int EgoHalfSigmaSteps,
    float MinimumGeneticCompatibility);

public sealed record RecruitmentAspirantRow(
    int Id,
    string Designation,
    string Phase,
    string Age,
    float TrainingProgress,
    bool CanBecomeNeophyte);

public sealed record RecruitmentCandidateRow(
    int Id,
    string Designation,
    string Age,
    float GeneticCompatibility);

public sealed record ScoutPromotionRow(
    int SoldierId,
    string Name,
    bool IsReady);

public sealed record ScoutSquadRow(
    int Id,
    string Label,
    string TrainingOptionKey,
    string ReadinessReport,
    IReadOnlyList<ScoutPromotionRow> PromotionRows,
    SquadRowViewModel CommonRow = null);

public sealed class RecruitmentScreenSnapshot
{
    public bool IsUnlocked { get; init; }
    public bool IsSetupComplete { get; init; }
    public string HomeWorldName { get; init; }
    public long ChapterPopulation { get; init; }
    public int Requisition { get; init; }
    public ushort Geneseed { get; init; }
    public RecruitmentStaffSummary Staff { get; init; }
    public RecruitmentDoctrineDraft Doctrine { get; init; }
    public RecruitmentForecast Forecast { get; init; }
    public IReadOnlyList<RecruitmentCandidateRow> Candidates { get; init; } = [];
    public IReadOnlyList<RecruitmentAspirantRow> Aspirants { get; init; } = [];
    public IReadOnlyList<ScoutTrainingOption> ScoutTrainingOptions { get; init; } = [];
    public IReadOnlyList<ScoutSquadRow> ScoutSquads { get; init; } = [];
    public IReadOnlyList<string> RecentEvents { get; init; } = [];
}

public partial class TrainingUnitScreenController : MainScreenController
{
    private readonly RecruitmentStaffService _staffService = new();
    private readonly RecruitmentForecastService _forecastService = new();
    private readonly SquadRowViewModelBuilder _squadRowBuilder = new();
    private TrainingUnitScreenView _view;
    private Squad _selectedSquad;
    private RecruitmentDoctrineDraft _draft;

    public event EventHandler<int> SoldierLinkClicked;
    public event EventHandler CampaignChanged;
    public event EventHandler ManageAdministrativeStaffRequested;
    public event EventHandler<int> NeophytePlacementRequested;
    public event EventHandler<int> Phase13PromotionRequested;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<TrainingUnitScreenView>("TrainingUnitScreenView");
        _view.LinkClicked += OnLinkClicked;
        _view.SquadButtonPressed += OnSquadButtonPressed;
        _view.TrainingOptionSelected += OnTrainingOptionSelected;
        _view.DoctrineChanged += OnDoctrineChanged;
        _view.DoctrineConfirmed += OnDoctrineConfirmed;
        _view.ManageAdministrativeStaffRequested += (sender, e) =>
            ManageAdministrativeStaffRequested?.Invoke(this, e);
        _view.NeophytePlacementRequested += (sender, aspirantId) =>
            NeophytePlacementRequested?.Invoke(this, aspirantId);
        _view.Phase13PromotionRequested += (sender, soldierId) =>
            Phase13PromotionRequested?.Invoke(this, soldierId);
        RefreshFromExternalChange();
    }

    public void RefreshFromExternalChange()
    {
        PlayerForce force = GameDataSingleton.Instance.Sector?.PlayerForce;
        RecruitmentProgram program = force?.RecruitmentProgram;
        if (program == null)
        {
            _draft = null;
            _view.RenderLockedState(
                "The Chapter has no Home World. The 10th Company will establish its "
                + "recruitment program when the Promised World is liberated.");
            PopulateScoutSquadList();
            return;
        }

        _staffService.Synchronize(force, GameDataSingleton.Instance.GameRulesData);
        _draft ??= CreateDraft(program);
        if (program.IsSetupComplete)
        {
            // External changes, including a loaded campaign, become the new baseline.
            _draft = CreateDraft(program);
        }

        Planet homeWorld = GetHomeWorld(force, program);
        long population = GetChapterPopulation(homeWorld, force.Faction.Id);
        float reputation = GetChapterReputation(homeWorld, force.Faction.Id);
        RecruitmentProgram previewProgram = CreatePreviewProgram(program, _draft);
        RecruitmentForecast forecast = _forecastService.Calculate(
            previewProgram,
            new RecruitmentForecastInput
            {
                ChapterHomeWorldPopulation = population,
                // Organic growth is recorded inside the active turn processor. Between turns,
                // the historic birth proxy provides the stable planning estimate.
                OrganicPopulationGrowth = 0,
                PlayerReputation = reputation
            });

        List<ScoutSquadRow> scoutSquads = BuildScoutSquadRows();
        if (_selectedSquad != null
            && scoutSquads.All(row => row.Id != _selectedSquad.Id))
        {
            _selectedSquad = null;
        }

        RecruitmentStaffSummary staff = new(
            program.StaffAssignments.Count(a => a.Role == RecruitmentStaffRole.ScoutSergeant),
            program.StaffAssignments.Count(a => a.Role == RecruitmentStaffRole.Apothecary),
            program.StaffAssignments.Count(a => a.Role == RecruitmentStaffRole.Chaplain));

        RecruitmentScreenSnapshot snapshot = new()
        {
            IsUnlocked = true,
            IsSetupComplete = program.IsSetupComplete,
            HomeWorldName = homeWorld?.Name ?? "Unknown Home World",
            ChapterPopulation = population,
            Requisition = force.Army.Requisition,
            Geneseed = force.GeneseedStockpile,
            Staff = staff,
            Doctrine = _draft,
            Forecast = forecast,
            Candidates = program.QualifiedCandidates
                .OrderBy(candidate => candidate.QualifiedDate)
                .ThenBy(candidate => candidate.Id)
                .Select(candidate => new RecruitmentCandidateRow(
                    candidate.Id,
                    candidate.InductionDesignation,
                    FormatAge(candidate.BirthDate),
                    candidate.GeneticCompatibility))
                .ToList(),
            Aspirants = program.Aspirants
                .OrderBy(aspirant => aspirant.Phase)
                .ThenBy(aspirant => aspirant.BirthDate)
                .ThenBy(aspirant => aspirant.Id)
                .Select(aspirant => new RecruitmentAspirantRow(
                    aspirant.Id,
                    aspirant.InductionDesignation,
                    FormatPhase(aspirant.Phase),
                    FormatAge(aspirant.BirthDate),
                    aspirant.TrainingProgress,
                    aspirant.Phase == RecruitmentPhase.Phase12))
                .ToList(),
            ScoutTrainingOptions = GameDataSingleton.Instance.GameRulesData
                .ScoutTrainingOptions.Options,
            ScoutSquads = scoutSquads,
            RecentEvents = program.ProgramEvents
                .OrderByDescending(programEvent => programEvent.Date)
                .Take(8)
                .Select(FormatProgramEvent)
                .ToList()
        };

        _view.Render(snapshot, _selectedSquad?.Id);
    }

    public void OpenMandatorySetup()
    {
        RefreshFromExternalChange();
        RecruitmentProgram program =
            GameDataSingleton.Instance.Sector?.PlayerForce?.RecruitmentProgram;
        if (program is { IsSetupComplete: false })
        {
            _view.ShowRecruitmentView();
            _view.SetSetupMode(true);
        }
    }

    public override void RequestClose()
    {
        RecruitmentProgram program =
            GameDataSingleton.Instance.Sector?.PlayerForce?.RecruitmentProgram;
        if (program is { IsSetupComplete: false })
        {
            _view.ShowSetupValidation(
                "The Master of Recruitment must establish the program before continuing.");
            return;
        }
        base.RequestClose();
    }

    private void OnDoctrineChanged(object sender, RecruitmentDoctrineDraft doctrine)
    {
        _draft = doctrine;
        RefreshPreviewOnly();
    }

    private void RefreshPreviewOnly()
    {
        // Preserve the staged doctrine through the normal snapshot rebuild.
        RecruitmentDoctrineDraft draft = _draft;
        RefreshFromExternalChange();
        _draft = draft;

        PlayerForce force = GameDataSingleton.Instance.Sector?.PlayerForce;
        RecruitmentProgram program = force?.RecruitmentProgram;
        if (force == null || program == null)
        {
            return;
        }

        _staffService.Synchronize(force, GameDataSingleton.Instance.GameRulesData);
        Planet homeWorld = GetHomeWorld(force, program);
        RecruitmentProgram preview = CreatePreviewProgram(program, _draft);
        RecruitmentForecast forecast = _forecastService.Calculate(
            preview,
            new RecruitmentForecastInput
            {
                ChapterHomeWorldPopulation =
                    GetChapterPopulation(homeWorld, force.Faction.Id),
                OrganicPopulationGrowth = 0,
                PlayerReputation = GetChapterReputation(homeWorld, force.Faction.Id)
            });
        _view.UpdateForecast(_draft, forecast);
    }

    private void OnDoctrineConfirmed(object sender, EventArgs e)
    {
        PlayerForce force = GameDataSingleton.Instance.Sector?.PlayerForce;
        RecruitmentProgram program = force?.RecruitmentProgram;
        if (force == null || program == null || _draft == null)
        {
            return;
        }

        _staffService.Synchronize(force, GameDataSingleton.Instance.GameRulesData);
        RecruitmentStaffSummary staff = new(
            program.StaffAssignments.Count(a => a.Role == RecruitmentStaffRole.ScoutSergeant),
            program.StaffAssignments.Count(a => a.Role == RecruitmentStaffRole.Apothecary),
            program.StaffAssignments.Count(a => a.Role == RecruitmentStaffRole.Chaplain));
        if (!staff.IsComplete)
        {
            _view.ShowSetupValidation(
                "The 10th Company HQ must include at least one Scout Sergeant, "
                + "one Apothecary, and one Chaplain or Judiciar. Reassign them on "
                + "the Chapter screen, then return here.");
            return;
        }
        if (!IsDoctrineValid(_draft))
        {
            _view.ShowSetupValidation(
                "All recruitment thresholds must remain within the allowed range.");
            return;
        }

        program.Policy = _draft.Policy;
        program.AttributeFilters.StrengthHalfSigmaSteps = _draft.StrengthHalfSigmaSteps;
        program.AttributeFilters.ConstitutionHalfSigmaSteps = _draft.ConstitutionHalfSigmaSteps;
        program.AttributeFilters.IntelligenceHalfSigmaSteps = _draft.IntelligenceHalfSigmaSteps;
        program.AttributeFilters.DexterityHalfSigmaSteps = _draft.DexterityHalfSigmaSteps;
        program.AttributeFilters.EgoHalfSigmaSteps = _draft.EgoHalfSigmaSteps;
        program.MinimumGeneticCompatibility = _draft.MinimumGeneticCompatibility;
        program.IsSetupComplete = true;
        CampaignChanged?.Invoke(this, EventArgs.Empty);
        _view.ShowSetupValidation(string.Empty);
        RefreshFromExternalChange();
    }

    private void PopulateScoutSquadList()
    {
        _view.PopulateScoutSquads(BuildScoutSquadRows(), _selectedSquad?.Id);
    }

    private List<ScoutSquadRow> BuildScoutSquadRows()
    {
        return OrderScoutSquads(GetScoutSquads())
            .Select(squad => new ScoutSquadRow(
                squad.Id,
                GetSquadListLabel(
                    squad,
                    GameDataSingleton.Instance.GameRulesData.ScoutTrainingOptions),
                squad.TrainingOptionKey,
                BuildReadinessReport(squad),
                squad.Members
                    .OfType<PlayerSoldier>()
                    .Where(soldier => !soldier.Template.IsSquadLeader)
                    .Where(soldier => soldier.GeneticCompatibility.HasValue)
                    .Select(soldier => new ScoutPromotionRow(
                        soldier.Id,
                        soldier.Name,
                        GetReadinessLevel(GetLatestEvaluation(soldier)) > 0))
                    .ToList(),
                _squadRowBuilder.Build(
                    squad,
                    new SquadRowContext(
                        SquadRowContextKind.RecruiterTraining,
                        SquadRowAction.Inspect,
                        isSelected: _selectedSquad?.Id == squad.Id,
                        isSelectable: true,
                        isEnabled: true,
                        contextBadge: "SCOUT TRAINING"),
                    GameDataSingleton.Instance?.Sector?.PlayerForce?.RecruitmentProgram)))
            .ToList();
    }

    private void OnSquadButtonPressed(object sender, int squadId)
    {
        _selectedSquad = GetScoutSquads().FirstOrDefault(squad => squad.Id == squadId);
        PopulateScoutSquadList();
    }

    private void OnTrainingOptionSelected(object sender, string optionKey)
    {
        if (_selectedSquad == null || _selectedSquad.TrainingOptionKey == optionKey)
        {
            return;
        }

        GameRulesData rules = GameDataSingleton.Instance.GameRulesData;
        rules.ScoutTrainingOptions.GetRequired(optionKey);
        _selectedSquad.TrainingOptionKey = optionKey;
        CampaignChanged?.Invoke(this, EventArgs.Empty);
        PopulateScoutSquadList();
    }

    private static List<Squad> GetScoutSquads()
    {
        return GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle.GetAllSquads()
            .Where(IsTrainingSquad)
            .ToList();
    }

    internal static bool IsTrainingSquad(Squad squad)
    {
        SquadTypes type = squad?.SquadTemplate?.SquadType ?? SquadTypes.None;
        return squad?.CanAcceptSquadOrder == true
            && (type & SquadTypes.Scout) != 0
            && (type & SquadTypes.HQ) == 0;
    }

    internal static IEnumerable<Squad> OrderScoutSquads(IEnumerable<Squad> squads)
    {
        return squads
            .OrderBy(FleetScreenController.GetSquadTypeOrder)
            .ThenBy(squad => squad.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(squad => squad.Id);
    }

    internal static string GetSquadListLabel(Squad squad)
    {
        return GetSquadListLabel(squad, null);
    }

    internal static string GetSquadListLabel(
        Squad squad,
        ScoutTrainingOptionCatalog trainingOptions)
    {
        string status = squad.CurrentOrders?.Mission != null
            ? "On Mission"
            : GetTrainingOptionName(squad.TrainingOptionKey, trainingOptions);
        return $"{squad.Name} ({status})";
    }

    private static string GetTrainingOptionName(
        string optionKey,
        ScoutTrainingOptionCatalog trainingOptions)
    {
        if (trainingOptions?.TryGet(optionKey, out ScoutTrainingOption option) == true)
        {
            return option.DisplayName;
        }

        // Compatibility fallback for small hand-built UI fixtures that do not construct a
        // rules catalog. Production snapshots always pass the loaded catalog above.
        return optionKey switch
        {
            ScoutTrainingOptionKeys.Physical => "Physical",
            ScoutTrainingOptionKeys.Vehicles => "Vehicles",
            ScoutTrainingOptionKeys.Melee => "Melee",
            ScoutTrainingOptionKeys.Ranged => "Ranged",
            ScoutTrainingOptionKeys.Balanced => "Balanced",
            _ => optionKey ?? ScoutTrainingOptionKeys.Balanced
        };
    }

    private RatingConsumerBindings RatingBindings =>
        GameDataSingleton.Instance?.GameRulesData?.RatingConsumers
        ?? RatingConsumerBindings.CreateDefault();

    private string BuildReadinessReport(Squad squad)
    {
        if (squad.Members.Count == 0)
        {
            return "This squad has no members.";
        }

        return string.Join(
            "\n\n",
            squad.Members
                .OfType<PlayerSoldier>()
                .Where(soldier => !soldier.Template.IsSquadLeader)
                .Select(soldier => GetScoutDescription(
                    soldier.Id,
                    soldier.Name,
                    GetLatestEvaluation(soldier))));
    }

    private static SoldierEvaluation GetLatestEvaluation(PlayerSoldier soldier)
    {
        return soldier.SoldierEvaluationHistory.LastOrDefault();
    }

    private int GetReadinessLevel(SoldierEvaluation evaluation)
    {
        if (evaluation == null)
        {
            return 0;
        }
        float rangedRating = RatingBindings.Get(evaluation, RatingConsumerRole.RangedCombat);
        float meleeRating = RatingBindings.Get(evaluation, RatingConsumerRole.MeleeCombat);
        float leadershipRating = RatingBindings.Get(evaluation, RatingConsumerRole.CommandLeadership);
        if (rangedRating > 105 && meleeRating < 90)
        {
            return 1;
        }
        if (rangedRating > 105 && meleeRating > 90)
        {
            if (rangedRating > 110 && meleeRating > 95)
            {
                return leadershipRating > 55 ? 4 : 3;
            }
            return 2;
        }
        return 0;
    }

    private string GetScoutDescription(
        int id,
        string name,
        SoldierEvaluation evaluation)
    {
        string nameMarkup = $"[url={id}]{name}[/url]";
        return GetReadinessLevel(evaluation) switch
        {
            4 => nameMarkup + " is ready for his Black Carapace and assignment to a "
                + "Devastator Squad; I believe he will rise to be a Sergeant himself "
                + "in short order.",
            2 or 3 => nameMarkup + " is ready for his Black Carapace and assignment "
                + "to a Devastator Squad; I believe he will rise through the ranks quickly.",
            1 => nameMarkup + " is ready for his Black Carapace and assignment to "
                + "a Devastator Squad.",
            _ => nameMarkup + " is not ready to become a Battle Brother, and should "
                + "acquire more seasoning before taking the Black Carapace."
        };
    }

    private void OnLinkClicked(object sender, Variant meta)
    {
        SoldierLinkClicked?.Invoke(this, meta.AsInt32());
    }

    private static RecruitmentDoctrineDraft CreateDraft(RecruitmentProgram program)
    {
        RecruitmentAttributeFilters filters = program.AttributeFilters;
        return new RecruitmentDoctrineDraft(
            program.Policy,
            filters.StrengthHalfSigmaSteps,
            filters.ConstitutionHalfSigmaSteps,
            filters.IntelligenceHalfSigmaSteps,
            filters.DexterityHalfSigmaSteps,
            filters.EgoHalfSigmaSteps,
            program.MinimumGeneticCompatibility);
    }

    private static RecruitmentProgram CreatePreviewProgram(
        RecruitmentProgram source,
        RecruitmentDoctrineDraft doctrine)
    {
        RecruitmentProgram preview = new()
        {
            Id = source.Id,
            HomeWorldPlanetId = source.HomeWorldPlanetId,
            EstablishedDate = source.EstablishedDate,
            LastProcessedDate = source.LastProcessedDate,
            IsSetupComplete = source.IsSetupComplete,
            Policy = doctrine.Policy,
            WorldType = source.WorldType,
            MinimumGeneticCompatibility = doctrine.MinimumGeneticCompatibility,
            AttributeFilters = new RecruitmentAttributeFilters
            {
                StrengthHalfSigmaSteps = doctrine.StrengthHalfSigmaSteps,
                ConstitutionHalfSigmaSteps = doctrine.ConstitutionHalfSigmaSteps,
                IntelligenceHalfSigmaSteps = doctrine.IntelligenceHalfSigmaSteps,
                DexterityHalfSigmaSteps = doctrine.DexterityHalfSigmaSteps,
                EgoHalfSigmaSteps = doctrine.EgoHalfSigmaSteps
            }
        };
        preview.StaffAssignments.AddRange(source.StaffAssignments);
        preview.UnscreenedCohorts.AddRange(source.UnscreenedCohorts);
        preview.QualifiedCandidates.AddRange(source.QualifiedCandidates);
        preview.Aspirants.AddRange(source.Aspirants);
        return preview;
    }

    internal static bool IsDoctrineValid(RecruitmentDoctrineDraft doctrine)
    {
        if (doctrine == null
            || !Enum.IsDefined(doctrine.Policy)
            || doctrine.MinimumGeneticCompatibility < 0
            || doctrine.MinimumGeneticCompatibility > 1)
        {
            return false;
        }

        return new[]
        {
            doctrine.StrengthHalfSigmaSteps,
            doctrine.ConstitutionHalfSigmaSteps,
            doctrine.IntelligenceHalfSigmaSteps,
            doctrine.DexterityHalfSigmaSteps,
            doctrine.EgoHalfSigmaSteps
        }.All(value =>
            value >= RecruitmentRules.MinimumAttributeFilterHalfSteps
            && value <= RecruitmentRules.MaximumAttributeFilterHalfSteps);
    }

    private static Planet GetHomeWorld(PlayerForce force, RecruitmentProgram program)
    {
        int planetId = force.HomeWorldPlanetId ?? program.HomeWorldPlanetId;
        return GameDataSingleton.Instance.Sector.Planets.TryGetValue(
            planetId, out Planet planet)
                ? planet
                : null;
    }

    internal static long GetChapterPopulation(Planet planet, int chapterFactionId)
    {
        if (planet == null)
        {
            return 0;
        }

        return planet.Regions
            .Where(region => region != null)
            .Sum(region =>
                region.RegionFactionMap.TryGetValue(
                    chapterFactionId, out RegionFaction presence)
                    && presence.IsPublic
                        ? presence.Population
                        : 0);
    }

    private static float GetChapterReputation(Planet planet, int chapterFactionId)
    {
        if (planet != null
            && planet.PlanetFactionMap.TryGetValue(
                chapterFactionId, out PlanetFaction planetFaction))
        {
            return planetFaction.PlayerReputation;
        }
        return 0;
    }

    private static string FormatAge(Date birthDate)
    {
        if (birthDate == null || GameDataSingleton.Instance.Date == null)
        {
            return "Unknown";
        }
        double age = GameDataSingleton.Instance.Date.GetWeeksDifference(birthDate) / 52.0;
        return $"{Math.Max(0, age):0.0} years";
    }

    private static string FormatPhase(RecruitmentPhase phase)
    {
        return phase == RecruitmentPhase.Phase0PreImplantation
            ? "Phase 0 — Pre-implantation"
            : phase == RecruitmentPhase.Phase13BlackCarapace
                ? "Phase 13 — Black Carapace"
                : $"Phase {(int)phase}";
    }

    private static string FormatProgramEvent(RecruitmentProgramEvent programEvent)
    {
        string count = programEvent.Count > 1 ? $" ×{programEvent.Count}" : string.Empty;
        string detail = string.IsNullOrWhiteSpace(programEvent.Detail)
            ? string.Empty
            : $": {programEvent.Detail}";
        return $"{programEvent.Date} — {programEvent.Type}{count}{detail}";
    }
}
