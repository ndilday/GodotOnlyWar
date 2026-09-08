using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.Squads;

namespace OnlyWar.Application;

internal sealed class TrainingScreenContext
{
    private const string RecruitmentLockedMessage =
        "The Chapter has no Home World. The 10th Company will establish its "
        + "recruitment program when the Promised World is liberated.";

    private readonly RecruitmentForecastService _forecastService = new();
    private readonly SquadRowViewModelBuilder _trainingRowBuilder = new();
    private readonly TrainingContext _training;

    internal TrainingScreenContext(TrainingContext training)
    {
        _training = training ?? throw new ArgumentNullException(nameof(training));
    }

    internal bool IsRecruitmentSetupComplete =>
        _training?.Program?.IsSetupComplete == true;

    internal RecruitmentDoctrineDraft QueryDoctrineDraft()
    {
        RecruitmentProgram program = _training?.Program;
        return program == null ? null : CreateDraft(program);
    }

    internal RecruitmentScreenSnapshot QueryRecruitmentScreen(
        RecruitmentDoctrineDraft draft, int? selectedSquadId)
    {
        TrainingContext training = _training;
        RecruitmentProgram program = training?.Program;
        if (program == null)
        {
            return new RecruitmentScreenSnapshot
            {
                IsUnlocked = false,
                LockedMessage = RecruitmentLockedMessage,
                ScoutSquads = QueryScoutSquads(selectedSquadId)
            };
        }

        training.SynchronizeStaff();
        // A completed program is authoritative: an external change, including a loaded campaign,
        // becomes the new baseline rather than keeping a stale staged edit.
        RecruitmentDoctrineDraft effective =
            program.IsSetupComplete || draft == null ? CreateDraft(program) : draft;

        Planet homeWorld = training.FindHomeWorld();
        long population = GetChapterPopulation(homeWorld, training.FactionId);

        return new RecruitmentScreenSnapshot
        {
            IsUnlocked = true,
            IsSetupComplete = program.IsSetupComplete,
            HomeWorldName = homeWorld?.Name ?? "Unknown Home World",
            ChapterPopulation = population,
            Requisition = training.Requisition,
            Geneseed = training.GeneseedStockpile,
            Staff = SummarizeStaff(program),
            Doctrine = effective,
            Forecast = ToView(CalculateForecast(training, program, homeWorld, effective)),
            Rules = BuildScreenRules(),
            Candidates = program.QualifiedCandidates
                .OrderBy(candidate => candidate.QualifiedDate)
                .ThenBy(candidate => candidate.Id)
                .Select(candidate => new RecruitmentCandidateRow(
                    candidate.Id,
                    candidate.InductionDesignation,
                    FormatAge(training.CurrentDate, candidate.BirthDate),
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
                    FormatAge(training.CurrentDate, aspirant.BirthDate),
                    aspirant.TrainingProgress,
                    aspirant.Phase == RecruitmentPhase.Phase12))
                .ToList(),
            ScoutTrainingOptions = training.ScoutTrainingOptions.Options
                .Select(option => new ScoutTrainingOptionView(option.Key, option.DisplayName))
                .ToList(),
            ScoutSquads = QueryScoutSquads(selectedSquadId),
            RecentEvents = program.ProgramEvents
                .OrderByDescending(programEvent => programEvent.Date)
                .Take(8)
                .Select(FormatProgramEvent)
                .ToList()
        };
    }

    internal RecruitmentForecastView PreviewForecast(RecruitmentDoctrineDraft draft)
    {
        TrainingContext training = _training;
        RecruitmentProgram program = training?.Program;
        if (program == null || draft == null) return null;

        training.SynchronizeStaff();
        return ToView(CalculateForecast(training, program, training.FindHomeWorld(), draft));
    }

    internal IReadOnlyList<ScoutSquadRow> QueryScoutSquads(int? selectedSquadId)
    {
        TrainingContext training = _training;
        if (training?.Chapter == null) return [];

        return OrderScoutSquads(training.ChapterSquads.Where(IsTrainingSquad))
            .Select(squad => new ScoutSquadRow(
                squad.Id,
                DescribeSquadListLabel(squad, training.ScoutTrainingOptions),
                squad.TrainingOptionKey,
                BuildReadinessReport(training, squad),
                squad.Members
                    .OfType<PlayerSoldier>()
                    .Where(soldier => !soldier.Template.IsSquadLeader)
                    .Where(soldier => soldier.GeneticCompatibility.HasValue)
                    .Select(soldier => new ScoutPromotionRow(
                        soldier.Id,
                        soldier.Name,
                        GetReadinessLevel(
                            training.RatingConsumers, GetLatestEvaluation(soldier)) > 0))
                    .ToList(),
                _trainingRowBuilder.Build(
                    squad,
                    new SquadRowContext(
                        SquadRowContextKind.RecruiterTraining,
                        SquadRowAction.Inspect,
                        isSelected: selectedSquadId == squad.Id,
                        isSelectable: true,
                        isEnabled: true,
                        contextBadge: "SCOUT TRAINING"),
                    training.Program,
                    training.OperationalDoctrine)))
            .ToList();
    }

    internal TrainingCommandResult ConfirmDoctrine(
        RecruitmentDoctrineDraft draft)
    {
        TrainingContext training = _training;
        RecruitmentProgram program = training.Program;
        if (program == null || draft == null)
        {
            return TrainingCommandResult.Failed(RecruitmentLockedMessage);
        }

        training.SynchronizeStaff();
        if (!SummarizeStaff(program).IsComplete)
        {
            return TrainingCommandResult.Failed(
                "The 10th Company HQ must include at least one Scout Sergeant, "
                + "one Apothecary, and one Chaplain or Judiciar. Reassign them on "
                + "the Chapter screen, then return here.");
        }
        if (!IsDoctrineValid(draft))
        {
            return TrainingCommandResult.Failed(
                "All recruitment thresholds must remain within the allowed range.");
        }

        program.Policy = ToDomainPolicy(draft.Policy);
        program.AttributeFilters.StrengthHalfSigmaSteps = draft.StrengthHalfSigmaSteps;
        program.AttributeFilters.ConstitutionHalfSigmaSteps = draft.ConstitutionHalfSigmaSteps;
        program.AttributeFilters.IntelligenceHalfSigmaSteps = draft.IntelligenceHalfSigmaSteps;
        program.AttributeFilters.DexterityHalfSigmaSteps = draft.DexterityHalfSigmaSteps;
        program.AttributeFilters.EgoHalfSigmaSteps = draft.EgoHalfSigmaSteps;
        program.MinimumGeneticCompatibility = draft.MinimumGeneticCompatibility;
        program.IsSetupComplete = true;
        return TrainingCommandResult.Ok();
    }

    internal TrainingCommandResult SetScoutTrainingOption(
        int squadId, string optionKey)
    {
        TrainingContext training = _training;
        Squad squad = training.ChapterSquads
            .FirstOrDefault(candidate => candidate.Id == squadId && IsTrainingSquad(candidate));
        if (squad == null)
        {
            return TrainingCommandResult.Failed("That scout squad is no longer in training.");
        }
        if (squad.TrainingOptionKey == optionKey) return TrainingCommandResult.Failed(null);

        // Throws on an unknown key rather than silently recording an unrunnable regimen.
        training.ValidateTrainingOption(optionKey);
        squad.TrainingOptionKey = optionKey;
        return TrainingCommandResult.Ok();
    }

    internal static bool IsTrainingSquad(Squad squad)
    {
        SquadTypes type = squad?.SquadTemplate?.SquadType ?? SquadTypes.None;
        return squad?.CanAcceptSquadOrder == true
            && (type & SquadTypes.Scout) != 0
            && (type & SquadTypes.HQ) == 0;
    }

    internal static IEnumerable<Squad> OrderScoutSquads(IEnumerable<Squad> squads) =>
        squads
            .OrderBy(ForceOrdering.SquadTypeOrder)
            .ThenBy(squad => squad.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(squad => squad.Id);

    internal static string DescribeSquadListLabel(Squad squad) =>
        DescribeSquadListLabel(squad, null);

    internal static string DescribeSquadListLabel(
        Squad squad, ScoutTrainingOptionCatalog trainingOptions)
    {
        string status = squad.CurrentOrders?.Mission != null
            ? "On Mission"
            : GetTrainingOptionName(squad.TrainingOptionKey, trainingOptions);
        return $"{squad.Name} ({status})";
    }

    private static string GetTrainingOptionName(
        string optionKey, ScoutTrainingOptionCatalog trainingOptions)
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

    private RecruitmentForecast CalculateForecast(
        TrainingContext training,
        RecruitmentProgram program,
        Planet homeWorld,
        RecruitmentDoctrineDraft draft) =>
        _forecastService.Calculate(
            CreatePreviewProgram(program, draft),
            new RecruitmentForecastInput
            {
                ChapterHomeWorldPopulation = GetChapterPopulation(homeWorld, training.FactionId),
                // Organic growth is recorded inside the active turn processor. Between turns,
                // the historic birth proxy provides the stable planning estimate.
                OrganicPopulationGrowth = 0,
                PlayerReputation = GetChapterReputation(homeWorld, training.FactionId)
            });

    private static RecruitmentScreenRulesView BuildScreenRules() => new(
        RecruitmentRules.MinimumAttributeFilterHalfSteps,
        RecruitmentRules.MaximumAttributeFilterHalfSteps,
        RecruitmentRules.AttributeFilterStepSigma);

    private static RecruitmentPolicy ToDomainPolicy(RecruitmentPolicyChoice policy) => policy switch
    {
        RecruitmentPolicyChoice.VoluntaryPresentation =>
            RecruitmentPolicy.VoluntaryPresentation,
        RecruitmentPolicyChoice.PlanetaryTithe => RecruitmentPolicy.PlanetaryTithe,
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, null)
    };

    private static RecruitmentPolicyChoice FromDomainPolicy(RecruitmentPolicy policy) => policy switch
    {
        RecruitmentPolicy.VoluntaryPresentation => RecruitmentPolicyChoice.VoluntaryPresentation,
        RecruitmentPolicy.PlanetaryTithe => RecruitmentPolicyChoice.PlanetaryTithe,
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, null)
    };

    private static RecruitmentForecastView ToView(RecruitmentForecast forecast) => forecast == null
        ? null
        : new(
            forecast.ChildrenReachingRecruitmentAge,
            forecast.EligibleMaleCohort,
            forecast.UnscreenedBacklog,
            forecast.ScreeningDemand,
            forecast.NonGeneticScreeningCapacity,
            forecast.GeneticScreeningCapacity,
            forecast.SpiritualScreeningCapacity,
            forecast.ScreeningCapacity,
            forecast.ScreeningCoverage,
            forecast.ExpectedScreenedCandidates,
            forecast.PublicCompliance,
            forecast.WeeklyPublicSentimentChange,
            forecast.ExpectedCompliantCandidates,
            forecast.GeneticPassRate,
            forecast.AttributePassRate,
            forecast.ExpectedQualifiedCandidates,
            forecast.AspirantTrainingCapacity,
            forecast.AvailablePhaseZeroPlaces,
            forecast.QualifiedCandidateWaitlist,
            forecast.AvailablePhaseZeroPlacesAfterWaitlist,
            forecast.ExpectedNewPhaseZeroAdmissions,
            forecast.ExpectedCandidateOverflow,
            forecast.ExpectedPhase12Survivors,
            forecast.ExpectedPhase13BattleBrothers,
            forecast.ExpectedPhase12SurvivalRate,
            forecast.ExpectedPhase13SurvivalRate,
            forecast.WeeklyRequisitionCost,
            forecast.SourceAttributeMeanModifierSigma);

    private static RecruitmentStaffSummary SummarizeStaff(RecruitmentProgram program) => new(
        program.StaffAssignments.Count(a => a.Role == RecruitmentStaffRole.ScoutSergeant),
        program.StaffAssignments.Count(a => a.Role == RecruitmentStaffRole.Apothecary),
        program.StaffAssignments.Count(a => a.Role == RecruitmentStaffRole.Chaplain));

    private static RecruitmentDoctrineDraft CreateDraft(RecruitmentProgram program)
    {
        RecruitmentAttributeFilters filters = program.AttributeFilters;
        return new RecruitmentDoctrineDraft(
            FromDomainPolicy(program.Policy),
            filters.StrengthHalfSigmaSteps,
            filters.ConstitutionHalfSigmaSteps,
            filters.IntelligenceHalfSigmaSteps,
            filters.DexterityHalfSigmaSteps,
            filters.EgoHalfSigmaSteps,
            program.MinimumGeneticCompatibility);
    }

    private static RecruitmentProgram CreatePreviewProgram(
        RecruitmentProgram source, RecruitmentDoctrineDraft doctrine)
    {
        RecruitmentProgram preview = new()
        {
            Id = source.Id,
            HomeWorldPlanetId = source.HomeWorldPlanetId,
            EstablishedDate = source.EstablishedDate,
            LastProcessedDate = source.LastProcessedDate,
            IsSetupComplete = source.IsSetupComplete,
            Policy = ToDomainPolicy(doctrine.Policy),
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

    internal static long GetChapterPopulation(Planet planet, int chapterFactionId)
    {
        if (planet == null) return 0;

        return planet.Regions
            .Where(region => region != null)
            .Sum(region =>
                region.RegionFactionMap.TryGetValue(chapterFactionId, out RegionFaction presence)
                    && presence.IsPublic
                        ? presence.Population
                        : 0);
    }

    private static float GetChapterReputation(Planet planet, int chapterFactionId) =>
        planet != null
            && planet.PlanetFactionMap.TryGetValue(chapterFactionId, out PlanetFaction planetFaction)
                ? planetFaction.PlayerReputation
                : 0;

    private static string BuildReadinessReport(TrainingContext training, Squad squad)
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
                    training, soldier.Id, soldier.Name, GetLatestEvaluation(soldier))));
    }

    private static SoldierEvaluation GetLatestEvaluation(PlayerSoldier soldier) =>
        soldier.SoldierEvaluationHistory.LastOrDefault();

    private static int GetReadinessLevel(
        RatingConsumerBindings bindings, SoldierEvaluation evaluation)
    {
        if (evaluation == null) return 0;

        float rangedRating = bindings.Get(evaluation, RatingConsumerRole.RangedCombat);
        float meleeRating = bindings.Get(evaluation, RatingConsumerRole.MeleeCombat);
        float leadershipRating = bindings.Get(evaluation, RatingConsumerRole.CommandLeadership);
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

    private static string GetScoutDescription(
        TrainingContext training, int id, string name, SoldierEvaluation evaluation)
    {
        string nameMarkup = $"[url={id}]{name}[/url]";
        return GetReadinessLevel(training.RatingConsumers, evaluation) switch
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

    private static string FormatAge(Date currentDate, Date birthDate)
    {
        if (birthDate == null || currentDate == null)
        {
            return "Unknown";
        }

        double age = currentDate.GetWeeksDifference(birthDate) / 52.0;
        return $"{Math.Max(0, age):0.0} years";
    }

    private static string FormatPhase(RecruitmentPhase phase) =>
        phase == RecruitmentPhase.Phase0PreImplantation
            ? "Phase 0 — Pre-implantation"
            : phase == RecruitmentPhase.Phase13BlackCarapace
                ? "Phase 13 — Black Carapace"
                : $"Phase {(int)phase}";

    private static string FormatProgramEvent(RecruitmentProgramEvent programEvent)
    {
        string count = programEvent.Count > 1 ? $" ×{programEvent.Count}" : string.Empty;
        string detail = string.IsNullOrWhiteSpace(programEvent.Detail)
            ? string.Empty
            : $": {programEvent.Detail}";
        return $"{programEvent.Date} — {programEvent.Type}{count}{detail}";
    }
}




