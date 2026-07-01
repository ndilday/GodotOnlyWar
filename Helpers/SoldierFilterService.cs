using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    // Applies a set of AND-combined filter conditions to a roster and surfaces the distinct
    // values (roles, honors) available for the filter dialog to offer within a given scope.
    public class SoldierFilterService
    {
        public IReadOnlyList<ISoldier> Apply(IEnumerable<ISoldier> soldiers,
                                             IReadOnlyList<SoldierFilterCondition> conditions,
                                             Date currentDate)
        {
            if (soldiers == null)
            {
                return [];
            }
            if (conditions == null || conditions.Count == 0)
            {
                return soldiers.ToList();
            }

            return soldiers
                .Where(soldier => conditions.All(condition => Matches(soldier, condition, currentDate)))
                .ToList();
        }

        // Distinct roles present in scope, most senior first. Uses Template.Name so the
        // dialog offers exactly the roles the player can see in the list.
        public IReadOnlyList<string> GetAvailableRoles(IEnumerable<ISoldier> soldiers)
        {
            return soldiers
                .Select(s => s.Template)
                .GroupBy(t => t.Name)
                .OrderByDescending(g => g.Max(t => t.Rank))
                .ThenBy(g => g.Key)
                .Select(g => g.Key)
                .ToList();
        }

        // Distinct honor tiers earned by anyone in scope. The filter value is Type+Level so
        // Bronze/Silver/etc. awards of the same type remain separate choices.
        public IReadOnlyList<SoldierHonorFilterOption> GetAvailableHonors(IEnumerable<ISoldier> soldiers)
        {
            return soldiers
                .OfType<PlayerSoldier>()
                .SelectMany(s => s.SoldierAwards)
                .GroupBy(a => new { a.Type, a.Level })
                .OrderBy(g => g.Key.Type)
                .ThenByDescending(g => g.Key.Level)
                .Select(g => new SoldierHonorFilterOption(
                    g.Key.Type,
                    g.Key.Level,
                    g.OrderByDescending(a => a.DateAwarded).FirstOrDefault()?.Name))
                .ToList();
        }

        private static bool Matches(ISoldier soldier, SoldierFilterCondition condition, Date currentDate)
        {
            switch (condition.Field)
            {
                case SoldierFilterField.Rank:
                    bool sameRole = soldier.Template.Name == condition.TextValue;
                    return condition.Operator == SoldierFilterOperator.NotEquals ? !sameRole : sameRole;

                case SoldierFilterField.Honor:
                    bool hasHonor = soldier is PlayerSoldier player
                        && player.SoldierAwards.Any(a => MatchesHonor(a, condition.TextValue));
                    return condition.Operator == SoldierFilterOperator.DoesNotHave ? !hasHonor : hasHonor;

                case SoldierFilterField.TimeInService:
                case SoldierFilterField.TimeInRank:
                case SoldierFilterField.TimeInSquad:
                    return MatchesDuration(soldier, condition, currentDate);

                default:
                    return true;
            }
        }

        private static bool MatchesHonor(SoldierAward award, string filterValue)
        {
            if (filterValue == SoldierHonorFilterOption.ToValue(award.Type, award.Level))
            {
                return true;
            }

            // Tolerate conditions created before honor filters tracked level.
            return !string.IsNullOrWhiteSpace(filterValue)
                && !filterValue.Contains('|')
                && award.Type == filterValue;
        }

        private static bool MatchesDuration(ISoldier soldier, SoldierFilterCondition condition, Date currentDate)
        {
            // Duration history only exists on PlayerSoldier; anyone without it cannot satisfy
            // a duration threshold.
            if (soldier is not PlayerSoldier player)
            {
                return false;
            }

            int weeks = condition.Field switch
            {
                SoldierFilterField.TimeInService => SoldierDossierService.GetWeeksInService(player, currentDate),
                SoldierFilterField.TimeInRank => SoldierDossierService.GetWeeksInRank(player, currentDate),
                SoldierFilterField.TimeInSquad => SoldierDossierService.GetWeeksInSquad(player, currentDate),
                _ => -1
            };

            // A -1 span means the anchor or current date was unavailable; treat as no match.
            if (weeks < 0)
            {
                return false;
            }

            return condition.Operator == SoldierFilterOperator.AtMost
                ? weeks <= condition.ThresholdWeeks
                : weeks >= condition.ThresholdWeeks;
        }
    }
}
