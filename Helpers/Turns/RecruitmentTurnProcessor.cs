using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Advances the standing Home World recruitment program once per campaign week.
    /// Forecasting and mutation share the same rules; population-sized cohorts remain
    /// aggregate and only fully-qualified children become individual records.
    /// </summary>
    internal sealed class RecruitmentTurnProcessor
    {
        private const int PhaseZeroMinimumTrainingWeeks = 4;
        private const float WeeklyTrainingProgress = 1f;
        private const float WeeklyPrimaryAttributeGrowth = 0.015f;
        private const float WeeklySecondaryAttributeGrowth = 0.0075f;
        private const float CandidateAttributeBase = 10f;
        private const float CandidateAttributeSigma = 2f;
        private const float ImplantPhysicalGrowth = 0.25f;

        private readonly GameSession _session;
        private readonly PlanetTurnProcessor _planetProcessor;
        private readonly RecruitmentStaffService _staffService = new();
        private readonly RecruitmentForecastService _forecastService = new();

        internal RecruitmentTurnProcessor(
            GameSession session,
            PlanetTurnProcessor planetProcessor)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _planetProcessor = planetProcessor
                ?? throw new ArgumentNullException(nameof(planetProcessor));
        }

        internal RecruitmentTurnReport Process()
        {
            PlayerForce force = _session.Sector.PlayerForce;
            RecruitmentProgram program = force?.RecruitmentProgram;
            if (program == null || !program.IsSetupComplete)
            {
                return null;
            }
            if (program.LastProcessedDate?.Equals(_session.CurrentDate) == true)
            {
                return null;
            }

            _staffService.Synchronize(force, _session.Rules);
            Planet homeWorld = _session.Sector.GetPlanet(program.HomeWorldPlanetId);
            Faction chapter = force.Faction;
            long population = GetChapterPopulation(homeWorld, chapter.Id);
            float reputation = homeWorld.PlanetFactionMap.TryGetValue(
                chapter.Id, out PlanetFaction chapterPlanetFaction)
                    ? chapterPlanetFaction.PlayerReputation
                    : 0;
            long organicGrowth = _planetProcessor.GetOrganicPopulationGrowth(
                homeWorld.Id, chapter.Id);
            RecruitmentForecast forecast = _forecastService.Calculate(
                program,
                new RecruitmentForecastInput
                {
                    ChapterHomeWorldPopulation = population,
                    OrganicPopulationGrowth = organicGrowth,
                    PlayerReputation = reputation
                });

            if (force.Army.Requisition < forecast.WeeklyRequisitionCost)
            {
                const string reason = "Insufficient Requisition for assigned recruitment staff.";
                AddProgramEvent(program, RecruitmentEventType.ProgramPaused, 1, reason);
                int agedOutWhilePaused = AgeOutCandidates(program);
                DecayAndExpireCohorts(program);
                program.LastProcessedDate = CopyDate(_session.CurrentDate);
                return new RecruitmentTurnReport(
                    false, reason, 0, 0, 0, 0, 0, 0, agedOutWhilePaused);
            }

            force.Army.Requisition -= forecast.WeeklyRequisitionCost;
            if (chapterPlanetFaction != null)
            {
                chapterPlanetFaction.PlayerReputation = Math.Clamp(
                    chapterPlanetFaction.PlayerReputation
                    + (float)RecruitmentRules.GetWeeklyPublicSentimentChange(program.Policy),
                    0,
                    1);
            }

            int agedOut = AgeOutCandidates(program);
            DecayAndExpireCohorts(program);
            List<ScreenedSegment> screenedSegments =
                ApplyWeeklyScreening(program, forecast);
            int screened = (int)Math.Round(screenedSegments.Sum(segment => segment.Count));
            int qualified = GenerateQualifiedCandidates(
                program,
                forecast,
                screenedSegments);
            int admitted = AdmitCandidates(program);
            AgeAndTrainAspirants(program);
            int implantationCapacity =
                RecruitmentForecastService.CalculateImplantationCapacity(program);
            (int blackCarapace, int blackCarapaceDeaths, int capacityUsed) =
                AdvanceBlackCarapaceProcedures(
                    force, program, implantationCapacity);
            (int implantations, int deaths) = AdvanceImplantationPipeline(
                force,
                program,
                Math.Max(0, implantationCapacity - capacityUsed));
            implantations += blackCarapace;
            deaths += blackCarapaceDeaths;

            program.LastProcessedDate = CopyDate(_session.CurrentDate);
            return new RecruitmentTurnReport(
                true,
                null,
                forecast.WeeklyRequisitionCost,
                screened,
                qualified,
                admitted,
                implantations,
                deaths,
                agedOut);
        }

        private (int Completed, int Deaths, int CapacityUsed)
            AdvanceBlackCarapaceProcedures(
            PlayerForce force,
            RecruitmentProgram program,
            int capacity)
        {
            int completed = 0;
            int deaths = 0;
            int capacityUsed = 0;
            foreach (RecruitmentProcedure procedure in program.Procedures
                .Where(item => item.Type == RecruitmentProcedureType.BlackCarapace)
                .OrderBy(item => item.Id)
                .ToList())
            {
                bool apothecaryStillAssigned = program.StaffAssignments.Any(staff =>
                    staff.Role == RecruitmentStaffRole.Apothecary
                    && staff.SoldierId == procedure.AssignedApothecarySoldierId);
                if (!apothecaryStillAssigned)
                {
                    procedure.Status = RecruitmentProcedureStatus.Paused;
                    continue;
                }
                if (capacity <= 0)
                {
                    procedure.Status = RecruitmentProcedureStatus.Paused;
                    continue;
                }
                if (!force.Army.PlayerSoldierMap.TryGetValue(
                        procedure.SubjectId, out PlayerSoldier neophyte))
                {
                    program.Procedures.Remove(procedure);
                    continue;
                }
                if (GetSquadPlanetId(neophyte.AssignedSquad)
                    != program.HomeWorldPlanetId)
                {
                    procedure.Status = RecruitmentProcedureStatus.Paused;
                    continue;
                }

                if (!procedure.ReservedSquadId.HasValue)
                {
                    procedure.Status = RecruitmentProcedureStatus.Paused;
                    continue;
                }
                force.Army.PopulateSquadMap();
                if (!force.Army.SquadMap.TryGetValue(
                        procedure.ReservedSquadId.Value, out Squad target)
                    || GetSquadPlanetId(target) != program.HomeWorldPlanetId
                    || !HasDevastatorSeat(target))
                {
                    procedure.Status = RecruitmentProcedureStatus.Paused;
                    continue;
                }

                procedure.Status = RecruitmentProcedureStatus.InProgress;
                procedure.WeeksRemaining = Math.Max(0, procedure.WeeksRemaining - 1);
                capacity--;
                capacityUsed++;
                if (procedure.WeeksRemaining > 0)
                {
                    continue;
                }

                if (_session.Random.GetLinearDouble()
                    >= procedure.GeneticCompatibility)
                {
                    neophyte.AddEvent(new SoldierEvent(
                        CopyDate(_session.CurrentDate),
                        SoldierEventType.Death,
                        $"{neophyte.Name} died during Black Carapace implantation."));
                    neophyte.AddEvent(new SoldierEvent(
                        CopyDate(_session.CurrentDate),
                        SoldierEventType.GeneseedRecovery,
                        "No mature progenoid gene-seed was recovered."));
                    new PlayerBattleAftermathSink(force).MoveToFallenBrothers(neophyte);
                    program.ProgramEvents.Add(new RecruitmentProgramEvent
                    {
                        Date = CopyDate(_session.CurrentDate),
                        Type = RecruitmentEventType.AspirantDied,
                        Count = 1,
                        Detail = $"{neophyte.Name} died during Black Carapace implantation."
                    });
                    program.Procedures.Remove(procedure);
                    deaths++;
                    continue;
                }

                Squad oldSquad = neophyte.AssignedSquad;
                oldSquad?.RemoveSquadMember(neophyte);
                target.AddSquadMember(neophyte);
                neophyte.Template = _session.Rules.ChapterTemplates.DevastatorMarine;
                neophyte.AddEvent(new SoldierEvent(
                    CopyDate(_session.CurrentDate),
                    SoldierEventType.Promotion,
                    $"received the Black Carapace and joined {target.Name} as a Devastator Marine"));
                program.ProgramEvents.Add(new RecruitmentProgramEvent
                {
                    Date = CopyDate(_session.CurrentDate),
                    Type = RecruitmentEventType.BlackCarapaceCompleted,
                    Count = 1,
                    Detail = $"{neophyte.Name} survived the Black Carapace implantation."
                });
                program.ProgramEvents.Add(new RecruitmentProgramEvent
                {
                    Date = CopyDate(_session.CurrentDate),
                    Type = RecruitmentEventType.BattleBrotherPromoted,
                    Count = 1,
                    Detail = $"{neophyte.Name} entered {target.Name} as a Battle-Brother."
                });
                program.Procedures.Remove(procedure);
                completed++;
            }
            return (completed, deaths, capacityUsed);
        }

        private static int? GetSquadPlanetId(Squad squad) =>
            (squad?.CurrentRegion?.Planet
                ?? squad?.BoardedLocation?.Fleet?.Planet)?.Id;

        private bool HasDevastatorSeat(Squad squad)
        {
            if (squad?.IsOperational != true
                || squad.SquadTemplate != _session.Rules.ChapterTemplates.DevastatorSquad)
            {
                return false;
            }
            SquadTemplateElement slot = squad.SquadTemplate.Elements.FirstOrDefault(
                item => item.SoldierTemplate == _session.Rules.ChapterTemplates.DevastatorMarine);
            return slot != null
                && squad.Members.Count(member =>
                    member.Template == _session.Rules.ChapterTemplates.DevastatorMarine)
                    < slot.MaximumNumber;
        }

        private List<ScreenedSegment> ApplyWeeklyScreening(
            RecruitmentProgram program,
            RecruitmentForecast forecast)
        {
            List<ScreenedSegment> segments = [];
            double remainingCapacity = forecast.ScreeningCapacity;
            double newEligible = forecast.EligibleMaleCohort;
            double screenedNew = Math.Min(newEligible, remainingCapacity);
            if (screenedNew > 0)
            {
                segments.Add(new ScreenedSegment(null, screenedNew));
                remainingCapacity -= screenedNew;
            }

            double newBacklog = Math.Max(0, newEligible - screenedNew);
            if (newBacklog > 0)
            {
                program.UnscreenedCohorts.Add(new RecruitmentCohort
                {
                    Id = NextCohortId(program),
                    CreatedDate = CopyDate(_session.CurrentDate),
                    RemainingPopulation = newBacklog,
                    MinimumAgeAtCreation = 10,
                    MaximumAgeAtCreation = 10,
                    IsFoundingCohort = false
                });
            }

            foreach (RecruitmentCohort cohort in program.UnscreenedCohorts
                .OrderBy(cohort => cohort.CreatedDate?.GetTotalWeeks() ?? 0)
                .ThenBy(cohort => cohort.Id)
                .ToList())
            {
                if (remainingCapacity <= 0)
                {
                    break;
                }
                double fromCohort = Math.Min(
                    Math.Max(0, cohort.RemainingPopulation),
                    remainingCapacity);
                if (fromCohort <= 0)
                {
                    continue;
                }

                cohort.RemainingPopulation -= fromCohort;
                remainingCapacity -= fromCohort;
                segments.Add(new ScreenedSegment(cohort, fromCohort));
            }
            program.UnscreenedCohorts.RemoveAll(
                cohort => cohort.RemainingPopulation < 0.0001);
            return segments;
        }

        private int GenerateQualifiedCandidates(
            RecruitmentProgram program,
            RecruitmentForecast forecast,
            IReadOnlyList<ScreenedSegment> segments)
        {
            double totalScreened = segments.Sum(segment => segment.Count);
            if (totalScreened <= 0)
            {
                return 0;
            }
            double qualificationRate =
                forecast.PublicCompliance
                * forecast.GeneticPassRate
                * forecast.AttributePassRate;
            int count = StochasticRound(totalScreened * qualificationRate);
            if (count <= 0)
            {
                return 0;
            }

            double sourceModifier =
                RecruitmentRules.GetSourceAttributeMeanModifier(program.WorldType);
            for (int index = 0; index < count; index++)
            {
                ScreenedSegment source = SelectSegment(segments, totalScreened);
                int candidateId = NextCandidateId(program);
                program.QualifiedCandidates.Add(new RecruitmentCandidate
                {
                    Id = candidateId,
                    InductionDesignation =
                        $"{_session.CurrentDate}-{candidateId % 1000:D3}",
                    SourceWorldPlanetId = program.HomeWorldPlanetId,
                    BirthDate = GenerateBirthDate(source.Cohort),
                    QualifiedDate = CopyDate(_session.CurrentDate),
                    GeneticCompatibility = (float)_session.Random.GetDoubleInRange(
                        program.MinimumGeneticCompatibility, 1),
                    Attributes = GenerateCandidateAttributes(
                        program.AttributeFilters,
                        sourceModifier)
                });
            }

            AddProgramEvent(
                program,
                RecruitmentEventType.CandidateQualified,
                count,
                $"{count:N0} candidate{(count == 1 ? string.Empty : "s")} passed every screen.");
            return count;
        }

        private RecruitmentCandidateAttributes GenerateCandidateAttributes(
            RecruitmentAttributeFilters filters,
            double sourceModifier)
        {
            return new RecruitmentCandidateAttributes
            {
                Strength = GenerateFilteredAttribute(
                    filters.StrengthHalfSigmaSteps, sourceModifier),
                Constitution = GenerateFilteredAttribute(
                    filters.ConstitutionHalfSigmaSteps, sourceModifier),
                Intelligence = GenerateFilteredAttribute(
                    filters.IntelligenceHalfSigmaSteps, sourceModifier),
                Dexterity = GenerateFilteredAttribute(
                    filters.DexterityHalfSigmaSteps, sourceModifier),
                Ego = GenerateFilteredAttribute(
                    filters.EgoHalfSigmaSteps, sourceModifier)
            };
        }

        private float GenerateFilteredAttribute(int halfSigmaSteps, double sourceModifier)
        {
            double threshold =
                halfSigmaSteps * RecruitmentRules.AttributeFilterStepSigma;
            double lowerCdf = GaussianCalculator.ApproximateNormalCDF(
                (float)(threshold - sourceModifier));
            double probability = _session.Random.GetDoubleInRange(
                Math.Min(lowerCdf, 0.999999), 0.9999999);
            double standardized =
                GaussianCalculator.ApproximateInverseNormalCDF((float)probability)
                + sourceModifier;
            return CandidateAttributeBase
                + CandidateAttributeSigma * (float)standardized;
        }

        private int AdmitCandidates(RecruitmentProgram program)
        {
            int capacity = RecruitmentForecastService.CalculateTrainingCapacity(program);
            int openings = Math.Max(0, capacity - program.Aspirants.Count);
            if (openings == 0)
            {
                return 0;
            }

            List<RecruitmentCandidate> admitted = program.QualifiedCandidates
                .OrderBy(candidate => candidate.BirthDate?.GetTotalWeeks() ?? int.MaxValue)
                .ThenBy(candidate => candidate.QualifiedDate?.GetTotalWeeks() ?? int.MaxValue)
                .ThenBy(candidate => candidate.Id)
                .Take(openings)
                .ToList();
            foreach (RecruitmentCandidate candidate in admitted)
            {
                program.QualifiedCandidates.Remove(candidate);
                RecruitmentAspirant aspirant = new()
                {
                    Id = candidate.Id,
                    InductionDesignation = candidate.InductionDesignation,
                    SourceWorldPlanetId = candidate.SourceWorldPlanetId,
                    BirthDate = candidate.BirthDate,
                    AdmittedDate = CopyDate(_session.CurrentDate),
                    Phase = RecruitmentPhase.Phase0PreImplantation,
                    PhaseStartedDate = CopyDate(_session.CurrentDate),
                    WeeksInCurrentPhase = 0,
                    TrainingProgress = 0,
                    GeneticCompatibility = candidate.GeneticCompatibility,
                    Attributes = candidate.Attributes
                };
                aspirant.Events.Add(new RecruitmentAspirantEvent
                {
                    Date = CopyDate(_session.CurrentDate),
                    Type = RecruitmentEventType.AspirantAdmitted,
                    Detail = "Accepted into Phase 0 pre-implantation training."
                });
                program.Aspirants.Add(aspirant);
            }

            if (admitted.Count > 0)
            {
                AddProgramEvent(
                    program,
                    RecruitmentEventType.AspirantAdmitted,
                    admitted.Count,
                    $"{admitted.Count:N0} qualified candidate"
                    + $"{(admitted.Count == 1 ? string.Empty : "s")} entered Phase 0.");
            }
            return admitted.Count;
        }

        private void AgeAndTrainAspirants(RecruitmentProgram program)
        {
            foreach (RecruitmentAspirant aspirant in program.Aspirants)
            {
                aspirant.WeeksInCurrentPhase++;
                aspirant.TrainingProgress += WeeklyTrainingProgress;
                aspirant.Attributes.Strength += WeeklyPrimaryAttributeGrowth;
                aspirant.Attributes.Constitution += WeeklyPrimaryAttributeGrowth;
                aspirant.Attributes.Dexterity += WeeklyPrimaryAttributeGrowth;
                aspirant.Attributes.Intelligence += WeeklySecondaryAttributeGrowth;
                aspirant.Attributes.Ego += WeeklySecondaryAttributeGrowth;
            }
        }

        private (int Implantations, int Deaths) AdvanceImplantationPipeline(
            PlayerForce force,
            RecruitmentProgram program,
            int capacity)
        {
            int implantations = 0;
            List<RecruitmentAspirant> deaths = [];

            foreach (RecruitmentAspirant aspirant in program.Aspirants
                .Where(aspirant => aspirant.Phase != RecruitmentPhase.Phase12)
                .OrderByDescending(GetAgeYears)
                .ThenBy(aspirant => aspirant.Id)
                .ToList())
            {
                RecruitmentPhase next = aspirant.Phase == RecruitmentPhase.Phase0PreImplantation
                    ? RecruitmentPhase.Phase1
                    : (RecruitmentPhase)((int)aspirant.Phase + 1);
                double age = GetAgeYears(aspirant);
                RecruitmentRules.AgeWindow window = RecruitmentRules.GetPhaseAgeWindow(next);
                if (age >= window.MaximumAgeExclusive)
                {
                    deaths.Add(aspirant);
                    continue;
                }
                if (capacity <= 0
                    || !window.Contains(age)
                    || aspirant.WeeksInCurrentPhase < 1
                    || (aspirant.Phase == RecruitmentPhase.Phase0PreImplantation
                        && aspirant.TrainingProgress < PhaseZeroMinimumTrainingWeeks))
                {
                    continue;
                }
                if (next == RecruitmentPhase.Phase1)
                {
                    if (force.GeneseedStockpile == 0)
                    {
                        continue;
                    }
                    force.GeneseedStockpile--;
                }

                capacity--;
                if (_session.Random.GetLinearDouble() >= aspirant.GeneticCompatibility)
                {
                    deaths.Add(aspirant);
                    continue;
                }

                aspirant.Phase = next;
                aspirant.PhaseStartedDate = CopyDate(_session.CurrentDate);
                aspirant.WeeksInCurrentPhase = 0;
                aspirant.Attributes.Strength += ImplantPhysicalGrowth;
                aspirant.Attributes.Constitution += ImplantPhysicalGrowth;
                aspirant.Events.Add(new RecruitmentAspirantEvent
                {
                    Date = CopyDate(_session.CurrentDate),
                    Type = RecruitmentEventType.ImplantationCompleted,
                    Detail = $"Survived implantation Phase {(int)next}."
                });
                implantations++;
            }

            foreach (RecruitmentAspirant aspirant in deaths)
            {
                program.Aspirants.Remove(aspirant);
            }
            if (implantations > 0)
            {
                AddProgramEvent(
                    program,
                    RecruitmentEventType.ImplantationCompleted,
                    implantations,
                    $"{implantations:N0} implantation phase"
                    + $"{(implantations == 1 ? string.Empty : "s")} completed.");
            }
            if (deaths.Count > 0)
            {
                AddProgramEvent(
                    program,
                    RecruitmentEventType.AspirantDied,
                    deaths.Count,
                    $"{deaths.Count:N0} aspirant"
                    + $"{(deaths.Count == 1 ? string.Empty : "s")} died or exceeded an implantation window.");
            }
            return (implantations, deaths.Count);
        }

        private int AgeOutCandidates(RecruitmentProgram program)
        {
            List<RecruitmentCandidate> expired = program.QualifiedCandidates
                .Where(candidate =>
                    _session.CurrentDate.GetWeeksDifference(candidate.QualifiedDate)
                        >= RecruitmentRules.CandidateMaximumWaitWeeks)
                .ToList();
            foreach (RecruitmentCandidate candidate in expired)
            {
                program.QualifiedCandidates.Remove(candidate);
            }
            if (expired.Count > 0)
            {
                AddProgramEvent(
                    program,
                    RecruitmentEventType.CandidateAgedOut,
                    expired.Count,
                    $"{expired.Count:N0} qualified candidate"
                    + $"{(expired.Count == 1 ? string.Empty : "s")} aged out.");
            }
            return expired.Count;
        }

        private void DecayAndExpireCohorts(RecruitmentProgram program)
        {
            foreach (RecruitmentCohort cohort in program.UnscreenedCohorts.ToList())
            {
                int ageWeeks = _session.CurrentDate.GetWeeksDifference(cohort.CreatedDate);
                if (ageWeeks >= RecruitmentRules.FoundingCohortExpirationWeeks)
                {
                    program.UnscreenedCohorts.Remove(cohort);
                    continue;
                }
                if (cohort.IsFoundingCohort)
                {
                    cohort.RemainingPopulation *=
                        1 - RecruitmentRules.FoundingCohortWeeklyDecay;
                }
            }
        }

        private Date GenerateBirthDate(RecruitmentCohort cohort)
        {
            double ageYears;
            if (cohort == null)
            {
                ageYears = 10;
            }
            else
            {
                int elapsed = Math.Max(
                    0,
                    _session.CurrentDate.GetWeeksDifference(cohort.CreatedDate));
                double minimum = cohort.MinimumAgeAtCreation + elapsed / 52.0;
                double maximum = cohort.IsFoundingCohort
                    ? cohort.MaximumAgeAtCreation
                    : cohort.MaximumAgeAtCreation + elapsed / 52.0;
                maximum = Math.Max(minimum, maximum);
                ageYears = maximum <= minimum
                    ? minimum
                    : _session.Random.GetDoubleInRange(minimum, maximum);
            }

            int birthWeek = Math.Max(
                1,
                _session.CurrentDate.GetTotalWeeks()
                - (int)Math.Round(ageYears * 52));
            return Date.FromTotalWeeks(birthWeek);
        }

        private ScreenedSegment SelectSegment(
            IReadOnlyList<ScreenedSegment> segments,
            double total)
        {
            double roll = _session.Random.GetDoubleInRange(0, total);
            double cumulative = 0;
            foreach (ScreenedSegment segment in segments)
            {
                cumulative += segment.Count;
                if (roll < cumulative)
                {
                    return segment;
                }
            }
            return segments[^1];
        }

        private int StochasticRound(double expected)
        {
            if (expected <= 0)
            {
                return 0;
            }
            int whole = expected >= int.MaxValue
                ? int.MaxValue
                : (int)Math.Floor(expected);
            return whole < int.MaxValue
                && _session.Random.GetLinearDouble() < expected - whole
                    ? whole + 1
                    : whole;
        }

        private double GetAgeYears(RecruitmentAspirant aspirant)
        {
            return _session.CurrentDate.GetWeeksDifference(aspirant.BirthDate) / 52.0;
        }

        private void AddProgramEvent(
            RecruitmentProgram program,
            RecruitmentEventType type,
            int count,
            string detail)
        {
            program.ProgramEvents.Add(new RecruitmentProgramEvent
            {
                Date = CopyDate(_session.CurrentDate),
                Type = type,
                Count = count,
                Detail = detail
            });
        }

        private static long GetChapterPopulation(Planet planet, int chapterFactionId)
        {
            return planet.Regions
                .Where(region => region != null)
                .Sum(region =>
                    region.RegionFactionMap.TryGetValue(
                        chapterFactionId, out RegionFaction presence)
                            && presence.IsPublic
                                ? presence.Population
                                : 0);
        }

        private static int NextCohortId(RecruitmentProgram program) =>
            program.UnscreenedCohorts.Select(cohort => cohort.Id).DefaultIfEmpty(0).Max() + 1;

        private static int NextCandidateId(RecruitmentProgram program) =>
            program.QualifiedCandidates.Select(candidate => candidate.Id)
                .Concat(program.Aspirants.Select(aspirant => aspirant.Id))
                .DefaultIfEmpty(0)
                .Max() + 1;

        private static Date CopyDate(Date date) =>
            date == null ? null : new Date(date.Millenium, date.Year, date.Week);

        private sealed record ScreenedSegment(
            RecruitmentCohort Cohort,
            double Count);
    }
}
