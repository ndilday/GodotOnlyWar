using OnlyWar.Domain;
using System;
using System.Collections.Generic;

namespace OnlyWar.Campaign.Turns
{
    /// <summary>
    /// Preference-aware adapter over the shared command-attention evaluator. Preferences affect
    /// interruption only; CommandAttentionEvaluator.Evaluate remains the complete factual set used
    /// by the live Command Brief.
    /// </summary>
    public static class EndTurnPreflight
    {
        public static EndTurnPreflightReport Evaluate(
            Sector sector,
            EndTurnWarningPreferences preferences)
        {
            return EvaluateCore(sector, preferences, null);
        }

        public static EndTurnPreflightReport EvaluateWithRules(
            Sector sector,
            EndTurnWarningPreferences preferences,
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
            EndTurnWarningPreferences preferences,
            GameRulesData rules)
        {
            if (sector == null) throw new ArgumentNullException(nameof(sector));
            IReadOnlyList<CommandAttentionFact> facts =
                CommandAttentionEvaluator.Evaluate(sector, rules);
            return new EndTurnPreflightReport(
                CommandAttentionEvaluator.ToPreflightItems(facts, preferences));
        }
    }
}
