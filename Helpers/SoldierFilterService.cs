using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
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

            List<ISoldier> soldierList = soldiers.ToList();
            Dictionary<string, RoleRank> roleRanks = BuildRoleRankLookup(soldierList);

            return soldierList
                .Where(soldier => conditions.All(condition => Matches(soldier, condition, currentDate, roleRanks)))
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
                .ThenByDescending(g => g.Max(t => t.Subrank))
                .ThenBy(g => g.Key)
                .Select(g => g.Key)
                .ToList();
        }

        // Distinct honor tiers earned by anyone in scope. The filter value is Type+Level so
        // Bronze/Silver/etc. awards of the same type remain separate choices. Labels come from
        // the award tiers' name templates (weapon-agnostic) rather than any one soldier's
        // resolved award name, so a ranged honor reads the same regardless of which weapon
        // earned it. See BuildTierLabelLookup.
        public IReadOnlyList<SoldierHonorFilterOption> GetAvailableHonors(
            IEnumerable<ISoldier> soldiers,
            IReadOnlyList<RatingAwardTier> awardTiers = null)
        {
            Dictionary<(string Type, ushort Level), string> labels = BuildTierLabelLookup(awardTiers);
            List<PlayerSoldier> playerSoldiers = soldiers.OfType<PlayerSoldier>().ToList();

            List<SoldierHonorFilterOption> options = playerSoldiers
                .SelectMany(s => s.SoldierAwards)
                .GroupBy(a => new { a.Type, a.Level })
                .OrderBy(g => g.Key.Type)
                .ThenByDescending(g => g.Key.Level)
                .Select(g => new SoldierHonorFilterOption(
                    g.Key.Type,
                    g.Key.Level,
                    labels.TryGetValue((g.Key.Type, g.Key.Level), out string label)
                        ? label
                        : g.OrderByDescending(a => a.DateAwarded).FirstOrDefault()?.Name))
                .ToList();

            options.AddRange(GetAvailableFlagHonors(playerSoldiers, awardTiers));
            return options;
        }

        // Yes/no rating flags (Novice piety, medical/technical aptitude) are offered as
        // honor entries too. Like awards, a flag is only offered when someone in scope
        // carries it. The flag text matched is the tier's NameTemplate — exactly what
        // RatingCalculator wrote into the soldier's history.
        private static IEnumerable<SoldierHonorFilterOption> GetAvailableFlagHonors(
            IReadOnlyList<PlayerSoldier> soldiers,
            IReadOnlyList<RatingAwardTier> awardTiers)
        {
            if (awardTiers == null)
            {
                return [];
            }

            HashSet<string> flagsInScope = soldiers
                .SelectMany(s => s.SoldierEvents)
                .Where(e => e.Type == SoldierEventType.RatingFlag)
                .Select(e => e.Detail)
                .ToHashSet();

            return awardTiers
                .Where(t => t.Effect == RatingAwardEffect.HistoryFlag
                            && flagsInScope.Contains(t.NameTemplate))
                .OrderBy(t => t.RatingKey)
                .Select(t => SoldierHonorFilterOption.FromFlag(
                    t.NameTemplate, FlagLabel(t)));
        }

        // Short player-facing names for the known flag ratings; anything new falls back
        // to the flag text itself.
        private static string FlagLabel(RatingAwardTier tier) => tier.RatingKey switch
        {
            RatingKeys.Piety => "Novice (Devout)",
            RatingKeys.Medical => "Medical aptitude",
            RatingKeys.Tech => "Technical aptitude",
            _ => tier.NameTemplate
        };

        // Maps each award Type+Level to a display name taken from the tier's NameTemplate with
        // the {bestSkillInCategory} placeholder swapped for the (generic) award Type, so e.g.
        // "Gold {bestSkillInCategory} of the Emperor" becomes "Gold Gun of the Emperor" rather
        // than naming whichever weapon a sample soldier happened to earn it with.
        private static Dictionary<(string, ushort), string> BuildTierLabelLookup(
            IReadOnlyList<RatingAwardTier> awardTiers)
        {
            Dictionary<(string, ushort), string> labels = [];
            if (awardTiers == null)
            {
                return labels;
            }

            foreach (RatingAwardTier tier in awardTiers)
            {
                var key = (tier.AwardType, (ushort)tier.Level);
                if (labels.ContainsKey(key))
                {
                    continue;
                }
                labels[key] = tier.NameTemplate.Replace(
                    RatingAwardTier.BestSkillInCategoryPlaceholder, tier.AwardType);
            }
            return labels;
        }

        private static bool Matches(ISoldier soldier, SoldierFilterCondition condition, Date currentDate,
                                    IReadOnlyDictionary<string, RoleRank> roleRanks)
        {
            switch (condition.Field)
            {
                case SoldierFilterField.Rank:
                    return MatchesRank(soldier, condition, roleRanks);

                case SoldierFilterField.Honor:
                    // Has = holds an award of the selected type at or above the selected
                    // tier, or carries the selected rating flag; DoesNotHave is its negation.
                    bool hasHonor;
                    if (SoldierHonorFilterOption.TryParseFlag(condition.TextValue, out string flagText))
                    {
                        hasHonor = soldier is PlayerSoldier flagged
                            && flagged.SoldierEvents.Any(e => e.Type == SoldierEventType.RatingFlag
                                                              && e.Detail == flagText);
                    }
                    else
                    {
                        hasHonor = soldier is PlayerSoldier player
                            && player.SoldierAwards.Any(a => MatchesHonor(a, condition.TextValue));
                    }
                    return condition.Operator == SoldierFilterOperator.DoesNotHave ? !hasHonor : hasHonor;

                case SoldierFilterField.TimeInService:
                case SoldierFilterField.TimeInRank:
                case SoldierFilterField.TimeInSquad:
                    return MatchesDuration(soldier, condition, currentDate);

                default:
                    return true;
            }
        }

        private static Dictionary<string, RoleRank> BuildRoleRankLookup(IEnumerable<ISoldier> soldiers)
        {
            return soldiers
                .Select(s => s.Template)
                .GroupBy(t => t.Name)
                .ToDictionary(
                    g => g.Key,
                    g => new RoleRank(g.Max(t => t.Rank), g.Max(t => t.Subrank)));
        }

        private static bool MatchesRank(ISoldier soldier, SoldierFilterCondition condition,
                                        IReadOnlyDictionary<string, RoleRank> roleRanks)
        {
            bool sameRole = soldier.Template.Name == condition.TextValue;
            if (condition.Operator == SoldierFilterOperator.Equals)
            {
                return sameRole;
            }
            if (condition.Operator == SoldierFilterOperator.NotEquals)
            {
                return !sameRole;
            }

            if (!roleRanks.TryGetValue(condition.TextValue, out RoleRank target))
            {
                return false;
            }

            RoleRank actual = new(soldier.Template.Rank, soldier.Template.Subrank);
            return condition.Operator switch
            {
                SoldierFilterOperator.Above => actual.CompareTo(target) > 0,
                SoldierFilterOperator.Below => actual.CompareTo(target) < 0,
                _ => false
            };
        }

        private static bool MatchesHonor(SoldierAward award, string filterValue)
        {
            if (!SoldierHonorFilterOption.TryParse(filterValue, out string type, out ushort? level))
            {
                return false;
            }

            if (award.Type != type)
            {
                return false;
            }

            // "Has" means "at least this tier": a Gold award satisfies a Silver threshold..
            // Legacy/type-only values mean any tier of that honor; tiered values apply
            // the normal "at least" comparison.
            return !level.HasValue || award.Level >= level.Value;
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

        private readonly record struct RoleRank(byte Rank, byte Subrank)
        {
            public int CompareTo(RoleRank other)
            {
                int rankComparison = Rank.CompareTo(other.Rank);
                return rankComparison != 0 ? rankComparison : Subrank.CompareTo(other.Subrank);
            }
        }
    }
}
