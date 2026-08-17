using OnlyWar.Models;
using OnlyWar.Models.Command;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    public enum EndTurnWarningCategory
    {
        IdleDeployableSquads,
        LeaderlessSquads,
        ActionableTaskForces,
        SpecialMissionOpportunities,
        RecruitmentProgram
    }

    public sealed class EndTurnAttentionItem
    {
        public EndTurnWarningCategory Category { get; }
        public int EntityId { get; }
        public string Title { get; }
        public string Detail { get; }
        public string StableKey { get; }
        public CampaignNavigationTarget NavigationTarget { get; }
        public int? DeadlineWeek { get; }

        public EndTurnAttentionItem(
            EndTurnWarningCategory category,
            int entityId,
            string title,
            string detail)
            : this(category, entityId, title, detail, null, null, null)
        {
        }

        public EndTurnAttentionItem(
            EndTurnWarningCategory category,
            int entityId,
            string title,
            string detail,
            string stableKey,
            CampaignNavigationTarget navigationTarget,
            int? deadlineWeek)
        {
            Category = category;
            EntityId = entityId;
            Title = title ?? string.Empty;
            Detail = detail ?? string.Empty;
            StableKey = stableKey ?? $"{category}/{entityId}";
            NavigationTarget = navigationTarget;
            DeadlineWeek = deadlineWeek;
        }
    }

    public sealed class EndTurnPreflightReport
    {
        private readonly IReadOnlyList<EndTurnAttentionItem> _items;

        public IReadOnlyList<EndTurnAttentionItem> Items => _items;
        public bool RequiresConfirmation => _items.Count > 0;

        public EndTurnPreflightReport(IEnumerable<EndTurnAttentionItem> items)
        {
            _items = (items ?? Enumerable.Empty<EndTurnAttentionItem>()).ToList().AsReadOnly();
        }

        public IReadOnlyList<EndTurnAttentionItem> ForCategory(EndTurnWarningCategory category)
        {
            return _items.Where(item => item.Category == category).ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Preference-aware adapter over the shared command-attention evaluator. Preferences affect
    /// interruption only; CommandAttentionEvaluator.Evaluate remains the complete factual set used
    /// by the live Command Brief.
    /// </summary>
    public static class EndTurnPreflight
    {
        public static EndTurnPreflightReport Evaluate(
            Sector sector,
            Settings.EndTurnWarningPreferences preferences)
        {
            return EvaluateCore(sector, preferences, null);
        }

        internal static EndTurnPreflightReport EvaluateWithRules(
            Sector sector,
            Settings.EndTurnWarningPreferences preferences,
            GameRulesData rules)
        {
            return EvaluateCore(sector, preferences, rules);
        }

        internal static IReadOnlyList<CommandAttentionFact> EvaluateFacts(
            Sector sector,
            GameRulesData rules = null) =>
            CommandAttentionEvaluator.Evaluate(sector, rules);

        private static EndTurnPreflightReport EvaluateCore(
            Sector sector,
            Settings.EndTurnWarningPreferences preferences,
            GameRulesData rules)
        {
            if (sector == null) throw new ArgumentNullException(nameof(sector));
            IReadOnlyList<CommandAttentionFact> facts =
                CommandAttentionEvaluator.Evaluate(sector, rules);
            return new EndTurnPreflightReport(
                CommandAttentionEvaluator.ToPreflightItems(facts, preferences));
        }

        public static string GetCategoryTitle(EndTurnWarningCategory category) => category switch
        {
            EndTurnWarningCategory.IdleDeployableSquads => "Idle deployed squads",
            EndTurnWarningCategory.LeaderlessSquads => "Squads without a leader",
            EndTurnWarningCategory.ActionableTaskForces => "Task forces awaiting orders",
            EndTurnWarningCategory.SpecialMissionOpportunities => "Opportunities at risk",
            EndTurnWarningCategory.RecruitmentProgram => "Recruitment program",
            _ => "Unresolved attention"
        };

        public static string GetPreferenceLabel(EndTurnWarningCategory category) => category switch
        {
            EndTurnWarningCategory.IdleDeployableSquads => "Warn about idle deployed squads",
            EndTurnWarningCategory.LeaderlessSquads => "Warn about squads missing a leader",
            EndTurnWarningCategory.ActionableTaskForces => "Warn about task forces without destinations",
            EndTurnWarningCategory.SpecialMissionOpportunities => "Warn about unassigned special missions",
            EndTurnWarningCategory.RecruitmentProgram => "Warn about recruitment decisions and funding",
            _ => "Warn about this category"
        };
    }
}
