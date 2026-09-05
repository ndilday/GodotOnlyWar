using OnlyWar.Helpers.Turns;
using OnlyWar.Models.Events;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OnlyWar.Models.Command
{
    public enum CommandBriefPriority
    {
        Critical = 0,
        Actionable = 1,
        Monitor = 2
    }

    public enum CommandBriefCategory
    {
        RequiresOrders = 0,
        PetitionsAndOpportunities = 1,
        OperationsUnderway = 2,
        RecoveryAndReinforcement = 3,
        StrategicSituation = 4,
        Mandates = 5
    }

    public enum CommandLens
    {
        Brief,
        Chronicle
    }

    public enum ChronicleFilter
    {
        All,
        Defining,
        Battles,
        Brothers,
        Worlds,
        Chapter
    }

    public enum CampaignNavigationTargetKind
    {
        SectorMap,
        Planet,
        Region,
        Fleet,
        Squad,
        Soldier,
        Mission,
        Order,
        Diplomacy,
        Apothecarium,
        Recruitment,
        LastTurnReport,
        Unavailable
    }

    /// <summary>
    /// The only navigation value a Command card is allowed to carry. It is a stable semantic
    /// reference, not a control reference or a rendered-list index. DisplayNameSnapshot is retained
    /// for historical links whose entity may no longer be present in the live campaign.
    /// </summary>
    public sealed record CampaignNavigationTarget(
        CampaignNavigationTargetKind Kind,
        int? PrimaryId = null,
        int? FocusId = null,
        string Fallback = null,
        string DisplayNameSnapshot = null)
    {
        public bool IsAvailable => Kind != CampaignNavigationTargetKind.Unavailable;

        public static CampaignNavigationTarget UnavailableFor(
            CampaignNavigationTargetKind intendedKind,
            string displayName) =>
            new(CampaignNavigationTargetKind.Unavailable, null, null,
                $"{displayName ?? "This campaign record"} is no longer available.", displayName);
    }

    public sealed record CommandBriefRelatedLink(
        string Key,
        string Label,
        CampaignNavigationTarget Target);

    public sealed record CommandBriefItem(
        string StableKey,
        CommandBriefCategory Category,
        CommandBriefPriority Priority,
        string Title,
        string Summary,
        string DeadlineOrStatus,
        string IconKey,
        bool IsActionableNow,
        CampaignNavigationTarget PrimaryTarget,
        string ActionLabel,
        IReadOnlyList<CommandBriefRelatedLink> RelatedLinks,
        int? SortWeek,
        string SortDomainKey)
    {
        public CommandBriefItem(
            string stableKey,
            CommandBriefCategory category,
            CommandBriefPriority priority,
            string title,
            string summary,
            string deadlineOrStatus,
            string iconKey,
            bool isActionableNow,
            CampaignNavigationTarget primaryTarget,
            string actionLabel,
            IEnumerable<CommandBriefRelatedLink> relatedLinks = null,
            int? sortWeek = null,
            string sortDomainKey = null)
            : this(
                stableKey ?? throw new ArgumentNullException(nameof(stableKey)),
                category,
                priority,
                title ?? string.Empty,
                summary ?? string.Empty,
                deadlineOrStatus ?? string.Empty,
                iconKey ?? "archive",
                isActionableNow,
                primaryTarget,
                actionLabel ?? string.Empty,
                new ReadOnlyCollection<CommandBriefRelatedLink>(
                    (relatedLinks ?? Enumerable.Empty<CommandBriefRelatedLink>()).ToList()),
                sortWeek,
                sortDomainKey ?? stableKey)
        {
        }
    }

    public sealed class CommandBriefModel
    {
        private readonly IReadOnlyList<CommandBriefItem> _items;
        private readonly IReadOnlyList<CommandBriefCategory> _availableCategories;

        public IReadOnlyList<CommandBriefItem> Items => _items;
        public IReadOnlyList<CommandBriefCategory> AvailableCategories => _availableCategories;
        public bool HasActionableItems => _items.Any(item => item.IsActionableNow);

        public CommandBriefModel(
            IEnumerable<CommandBriefItem> items,
            IEnumerable<CommandBriefCategory> availableCategories = null)
        {
            _items = new ReadOnlyCollection<CommandBriefItem>(
                (items ?? Enumerable.Empty<CommandBriefItem>()).ToList());
            _availableCategories = new ReadOnlyCollection<CommandBriefCategory>(
                (availableCategories ?? Enum.GetValues<CommandBriefCategory>())
                    .Distinct()
                    .ToList());
        }

        public IReadOnlyList<CommandBriefItem> ForCategory(CommandBriefCategory? category)
        {
            return new ReadOnlyCollection<CommandBriefItem>(
                (category.HasValue
                    ? _items.Where(item => item.Category == category.Value)
                    : _items)
                .ToList());
        }
    }

    public sealed record ChronicleEntityLink(
        string Label,
        CampaignNavigationTarget Target,
        bool IsAvailable);

    public sealed record ChronicleEntryViewModel(
        long EntryId,
        int OccurredWeek,
        string DateLabel,
        CampaignEventImportance Importance,
        ChronicleFilter Category,
        string Title,
        string Body,
        IReadOnlyList<ChronicleEntityLink> Links,
        string RelatedBattleLabel);
}
