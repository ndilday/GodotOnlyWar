using OnlyWar.Helpers.Readiness;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.UI
{
    /// <summary>
    /// The one vocabulary used by every live squad presentation. The aliases keep the names
    /// readable at call sites while preserving the more precise injury/incapacitation wording in
    /// tooltips and reports.
    /// </summary>
    public enum SquadRowContextKind
    {
        Generic,
        PlanetaryOperations,
        Chapter,
        RecruiterTraining,
        Fleet,
        Apothecarium,
        Muster,
        BattleReview
    }

    public enum SquadRowAction
    {
        None,
        BeginOrder,
        Land,
        Embark,
        Transfer,
        Inspect
    }

    /// <summary>
    /// Screen-owned context layered on top of the common squad facts. It may add an action
    /// restriction, but it cannot redefine strength, leadership, or structural readiness.
    /// </summary>
    public sealed class SquadRowContext : SquadDeploymentContext
    {
        public new SquadRowAction Action => (SquadRowAction)base.Action;
        public SquadRowContextKind Kind { get; }
        public Region Origin { get; }
        public Region Target { get; }
        public bool IsSelected { get; }
        public bool IsSelectable { get; }
        public bool IsEnabled { get; }
        public string ContextBadge { get; }

        public SquadRowContext(
            SquadRowContextKind kind = SquadRowContextKind.Generic,
            SquadRowAction action = SquadRowAction.None,
            Region origin = null,
            Region target = null,
            bool isSelected = false,
            bool isSelectable = true,
            bool isEnabled = true,
            string contextBadge = null,
            IReadOnlyList<SquadReadinessBlocker> restrictions = null)
            : base((SquadDeploymentAction)action, restrictions)
        {
            Kind = kind;
            Origin = origin;
            Target = target;
            IsSelected = isSelected;
            IsSelectable = isSelectable;
            IsEnabled = isEnabled;
            ContextBadge = contextBadge;
        }

        public static SquadRowContext ForNewOrder(
            Region target = null,
            bool isSelected = false,
            bool isSelectable = true) =>
            new(SquadRowContextKind.PlanetaryOperations, SquadRowAction.BeginOrder,
                target: target, isSelected: isSelected, isSelectable: isSelectable);

        public static SquadRowContext ForLanding(
            Region target = null,
            bool isSelected = false,
            bool isSelectable = true) =>
            new(SquadRowContextKind.PlanetaryOperations, SquadRowAction.Land,
                target: target, isSelected: isSelected, isSelectable: isSelectable);

        public static SquadRowContext ForEmbark(
            Region origin = null,
            bool isSelected = false,
            bool isSelectable = true) =>
            new(SquadRowContextKind.PlanetaryOperations, SquadRowAction.Embark,
                origin: origin, isSelected: isSelected, isSelectable: isSelectable);
    }

    /// <summary>
    /// Canonical strength accounting for a live player squad.
    ///
    /// Full is template establishment, never below a legacy overstrength roster. Rostered is
    /// organizational membership, Present excludes an individual posting, Effective is the
    /// present combat-effective subset, and DutyReady is the subset permitted by the Chapter
    /// operational doctrine. Every unavailable member is assigned exactly one reason using the
    /// precedence in <see cref="ClassifyUnavailable"/>.
    /// </summary>
    public static class SquadReadinessPresentation
    {
        public static string CommitmentLabel(SquadCommitmentKind commitment) => commitment switch
        {
            SquadCommitmentKind.Order => "ORDER",
            SquadCommitmentKind.InTransit => "IN TRANSIT",
            SquadCommitmentKind.Training => "TRAINING",
            SquadCommitmentKind.Administrative => "ADMIN",
            _ => "FREE"
        };

        public static string LeaderLabel(SquadLeaderStatus status) => status switch
        {
            SquadLeaderStatus.Vacant => "NO LEADER",
            SquadLeaderStatus.Unavailable => "LEADER OUT",
            SquadLeaderStatus.NotRequired => string.Empty,
            _ => "LEADER READY"
        };

        public static string BlockerLabel(SquadReadinessBlocker blocker) =>
            ReadinessDescriptions.BlockerLabel(blocker);

        public static string UnavailableLabel(SquadUnavailableReason reason) => reason switch
        {
            SquadUnavailableReason.IndividualPosting => "POSTED",
            SquadUnavailableReason.ProcedureReservation => "PROCEDURE",
            SquadUnavailableReason.InjuryOrIncapacitation => "OUT",
            SquadUnavailableReason.DoctrineWithholding => "WITHHELD",
            _ => "UNAVAILABLE"
        };
    }

    /// <summary>
    /// Shared presentation facts for one live squad. A screen supplies context and owns actions;
    /// this model owns all facts that must remain legible on every row.
    /// </summary>
    public class SquadRowViewModel
    {
        public string Key { get; }
        public int? LiveSquadId { get; }
        public string Name { get; }
        public string Type { get; }
        public string IconKey { get; }
        public string ParentFormation { get; }
        public string Location { get; }
        public SquadStrengthSnapshot Strength { get; }
        public SquadLeaderStatus LeaderStatus { get; }
        public SquadCommitmentKind Commitment { get; }
        public string CommitmentLabel { get; }
        public SquadReadinessSnapshot Readiness { get; }
        public bool Selected { get; }
        public bool Selectable { get; }
        public bool Enabled { get; }
        public string DisabledReason { get; }
        public string ContextBadge { get; }
        public string Tooltip { get; }
        public int PresentationPriority { get; }

        public string StrengthLabel => $"{Strength.DutyReady}/{Strength.Full}";
        public string LeaderLabel => SquadReadinessPresentation.LeaderLabel(LeaderStatus);
        public string PrimaryStateLabel => Readiness.PrimaryBlocker != SquadReadinessBlocker.None
            ? SquadReadinessPresentation.BlockerLabel(Readiness.PrimaryBlocker)
            : LeaderLabel;

        public SquadRowViewModel(
            string key,
            string name,
            string type,
            string iconKey,
            string parentFormation,
            string location,
            SquadStrengthSnapshot strength,
            SquadLeaderStatus leaderStatus,
            SquadCommitmentKind commitment,
            SquadReadinessSnapshot readiness,
            int? liveSquadId = null,
            bool selected = false,
            bool selectable = true,
            bool enabled = true,
            string disabledReason = null,
            string contextBadge = null,
            string tooltip = null,
            int presentationPriority = 0)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Name = name ?? string.Empty;
            Type = type ?? string.Empty;
            IconKey = iconKey;
            ParentFormation = parentFormation ?? string.Empty;
            Location = location ?? string.Empty;
            Strength = strength ?? throw new ArgumentNullException(nameof(strength));
            LeaderStatus = leaderStatus;
            Commitment = commitment;
            CommitmentLabel = SquadReadinessPresentation.CommitmentLabel(commitment);
            Readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
            LiveSquadId = liveSquadId;
            Selected = selected;
            Selectable = selectable;
            Enabled = enabled;
            DisabledReason = disabledReason ??
                (Readiness.PrimaryBlocker == SquadReadinessBlocker.None
                    ? null : SquadReadinessPresentation.BlockerLabel(Readiness.PrimaryBlocker));
            ContextBadge = contextBadge;
            Tooltip = tooltip ?? string.Empty;
            PresentationPriority = presentationPriority;
        }
    }

    public sealed class ProjectedSquadRowViewModel : SquadRowViewModel
    {
        public int OutgoingDelta { get; }
        public int IncomingDelta { get; }
        public int FutureStrength { get; }
        public string ProvisionalKey { get; }

        public ProjectedSquadRowViewModel(
            SquadRowViewModel source,
            int outgoingDelta,
            int incomingDelta,
            int futureStrength,
            string provisionalKey)
            : base(
                source.Key,
                source.Name,
                source.Type,
                source.IconKey,
                source.ParentFormation,
                source.Location,
                source.Strength,
                source.LeaderStatus,
                source.Commitment,
                source.Readiness,
                source.LiveSquadId,
                source.Selected,
                source.Selectable,
                source.Enabled,
                source.DisabledReason,
                source.ContextBadge,
                source.Tooltip,
                source.PresentationPriority)
        {
            OutgoingDelta = outgoingDelta;
            IncomingDelta = incomingDelta;
            FutureStrength = futureStrength;
            ProvisionalKey = provisionalKey ?? source.Key;
        }
    }

    public sealed class BattleSquadRowViewModel : SquadRowViewModel
    {
        public int StartingStrength { get; }
        public int CurrentStrength { get; }
        public string MoraleLabel { get; }
        public string FatigueLabel { get; }

        public BattleSquadRowViewModel(
            SquadRowViewModel source,
            int startingStrength,
            int currentStrength,
            string moraleLabel = null,
            string fatigueLabel = null)
            : base(
                source.Key,
                source.Name,
                source.Type,
                source.IconKey,
                source.ParentFormation,
                source.Location,
                source.Strength,
                source.LeaderStatus,
                source.Commitment,
                source.Readiness,
                source.LiveSquadId,
                source.Selected,
                source.Selectable,
                enabled: false,
                disabledReason: SquadReadinessPresentation.BlockerLabel(
                    SquadReadinessBlocker.HistoricalFormation),
                source.ContextBadge,
                source.Tooltip,
                source.PresentationPriority)
        {
            StartingStrength = Math.Max(0, startingStrength);
            CurrentStrength = Math.Max(0, currentStrength);
            MoraleLabel = moraleLabel ?? string.Empty;
            FatigueLabel = fatigueLabel ?? string.Empty;
        }
    }

    public sealed class SquadRowViewModelBuilder
    {
        public SquadRowViewModel Build(
            Squad squad,
            SquadRowContext context = null,
            RecruitmentProgram program = null,
            ChapterOperationalDoctrine doctrine = null)
        {
            doctrine = CurrentCampaignReadinessContext.ResolveDoctrine(squad, doctrine);
            program = CurrentCampaignReadinessContext.ResolveProgram(squad, program);
            context ??= new SquadRowContext();
            SquadStrengthSnapshot strength = SquadStrengthSnapshotBuilder.Build(squad, program: program, doctrine: doctrine);
            SquadReadinessSnapshot readiness = SquadReadinessService.Evaluate(squad, context: context, program: program, doctrine: doctrine);
            string location = CampaignLocationLabel(squad);
            string type = squad?.SquadTemplate?.Name ?? "Formation";
            string unavailable = strength.PrimaryUnavailableReason.HasValue
                ? SquadReadinessPresentation.UnavailableLabel(
                    strength.PrimaryUnavailableReason.Value)
                : string.Empty;
            List<string> secondary = [];
            if (!string.IsNullOrWhiteSpace(unavailable)
                && strength.DutyReady < strength.Full
                && strength.PrimaryUnavailableReason.HasValue)
            {
                secondary.Add(unavailable);
            }
            if (readiness.LeaderStatus == SquadLeaderStatus.Vacant)
            {
                secondary.Add("NO LEADER");
            }
            else if (readiness.LeaderStatus == SquadLeaderStatus.Unavailable)
            {
                secondary.Add(LeaderAvailabilityLabel(squad, program, doctrine));
            }
            if (context.Action == SquadRowAction.BeginOrder
                && readiness.CanBeginDeployment)
            {
                secondary.Add("READY");
            }

            string tooltip = BuildTooltip(squad, strength, readiness, location,
                LeaderAvailabilityLabel(squad, program, doctrine));
            bool actionRequiresReadiness = context.Action != SquadRowAction.None
                && context.Action != SquadRowAction.Inspect;
            bool enabled = context.IsEnabled
                && (!actionRequiresReadiness
                    || readiness.PrimaryBlocker == SquadReadinessBlocker.None);
            string disabledReason = enabled
                ? null
                : readiness.PrimaryBlocker != SquadReadinessBlocker.None
                    ? SquadReadinessPresentation.BlockerLabel(readiness.PrimaryBlocker)
                    : context.Restrictions.FirstOrDefault() is SquadReadinessBlocker restriction
                        ? SquadReadinessPresentation.BlockerLabel(restriction)
                        : context.ContextBadge;
            return new SquadRowViewModel(
                squad == null ? "squad:unknown" : $"squad:{squad.Id}",
                squad?.Name ?? "Unknown formation",
                type,
                IconAtlas.GetSquadIconKey(squad?.SquadTemplate),
                squad?.ParentUnit?.Name ?? string.Empty,
                location,
                strength,
                readiness.LeaderStatus,
                readiness.Commitment,
                readiness,
                squad?.Id,
                context.IsSelected,
                context.IsSelectable,
                enabled,
                disabledReason,
                context.ContextBadge,
                tooltip,
                readiness.CanBeginDeployment ? 0 : 1);
        }

        public ProjectedSquadRowViewModel BuildProjected(
            SquadRowViewModel source,
            int outgoingDelta,
            int incomingDelta,
            int futureStrength,
            string provisionalKey) =>
            new(source, outgoingDelta, incomingDelta, futureStrength, provisionalKey);

        public BattleSquadRowViewModel BuildBattleSnapshot(
            BattleSquadSnapshot snapshot,
            int startingStrength = -1,
            int currentStrength = -1,
            string moraleLabel = null,
            string fatigueLabel = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            int current = currentStrength < 0 ? snapshot.Soldiers?.Count ?? 0 : currentStrength;
            int starting = startingStrength < 0 ? current : startingStrength;
            SquadRowViewModel source = snapshot.Squad != null
                ? Build(snapshot.Squad, new SquadRowContext(
                    SquadRowContextKind.BattleReview,
                    SquadRowAction.Inspect,
                    isSelectable: true,
                    isEnabled: false), null)
                : BuildSnapshotFallback(snapshot, current);
            return new BattleSquadRowViewModel(
                source, starting, current, moraleLabel, fatigueLabel);
        }

        private static SquadRowViewModel BuildSnapshotFallback(
            BattleSquadSnapshot snapshot,
            int currentStrength)
        {
            SquadStrengthSnapshot strength = new(
                currentStrength,
                currentStrength,
                currentStrength,
                currentStrength,
                currentStrength,
                0,
                0,
                new Dictionary<SquadUnavailableReason, int>());
            SquadReadinessSnapshot readiness = new(
                strength,
                SquadLeaderStatus.NotRequired,
                SquadReadinessState.NotApplicable,
                SquadCommitmentKind.Administrative,
                false,
                SquadReadinessBlocker.HistoricalFormation,
                [SquadReadinessBlocker.HistoricalFormation],
                []);
            return new SquadRowViewModel(
                $"battle-squad:{snapshot.Id}",
                snapshot.Name,
                "Battle formation",
                "tactical",
                string.Empty,
                "Historical battle",
                strength,
                SquadLeaderStatus.NotRequired,
                SquadCommitmentKind.Administrative,
                readiness,
                snapshot.Id,
                false,
                true,
                false,
                "HISTORICAL",
                null,
                $"{snapshot.Name}\nHistorical formation; deployment is not applicable.");
        }

        private static string CampaignLocationLabel(Squad squad)
        {
            if (squad?.DutyStation != null) return squad.DutyStation.ToString();
            if (squad?.BoardedLocation != null)
            {
                return squad.BoardedLocation.Fleet?.TravelPhase == FleetTravelPhase.InWarp
                    ? "In Warp"
                    : squad.BoardedLocation.Name;
            }
            return squad?.CurrentRegion?.Name ?? "Unlocated";
        }

        private static string BuildTooltip(
            Squad squad,
            SquadStrengthSnapshot strength,
            SquadReadinessSnapshot readiness,
            string location,
            string leaderAvailabilityLabel)
        {
            List<string> lines =
            [
                squad?.Name ?? "Unknown formation",
                $"Strength: {strength.DutyReady}/{strength.Full} duty-ready",
                $"Combat-effective: {strength.Effective}/{strength.Full}",
                $"Rostered: {strength.Rostered} · Present: {strength.Present}",
                $"Vacancies: {strength.Vacancies}",
                 $"Leader: {readiness.LeaderStatus switch
                 {
                     SquadLeaderStatus.Unavailable => leaderAvailabilityLabel,
                     _ => SquadReadinessPresentation.LeaderLabel(readiness.LeaderStatus)
                 }}",
                $"Commitment: {SquadReadinessPresentation.CommitmentLabel(readiness.Commitment)}",
                $"Location: {location}"
            ];
            if (strength.Unavailable > 0)
            {
                lines.Add($"Unavailable: {strength.Unavailable}");
                foreach (SquadUnavailableReason reason in Enum
                    .GetValues<SquadUnavailableReason>()
                    .Distinct())
                {
                    int count = strength.Count(reason);
                    if (count > 0)
                    {
                        lines.Add($"  {SquadReadinessPresentation.UnavailableLabel(reason)}: {count}");
                    }
                }
            }
            if (readiness.AllBlockers.Count > 0)
            {
                lines.Add($"Deployment: {string.Join(", ", readiness.AllBlockers
                    .Select(SquadReadinessPresentation.BlockerLabel))}");
            }
            return string.Join("\n", lines);
        }

        private static string LeaderAvailabilityLabel(Squad squad,
            RecruitmentProgram program, ChapterOperationalDoctrine doctrine)
        {
            DutyReadinessEvaluation evaluation = DutyReadinessService.Evaluate(
                squad?.SquadLeader, doctrine, program);
            return evaluation.ReasonCode == DutyReadinessReasonCode.ChapterInjuryThreshold
                ? "LEADER WITHHELD"
                : "LEADER OUT";
        }
    }
}
