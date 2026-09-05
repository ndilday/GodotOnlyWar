using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OnlyWar.Helpers
{
    public sealed record SoldierDossier(
        IReadOnlyList<ValueTuple<string, string>> Data,
        IReadOnlyList<string> History,
        IReadOnlyList<string> Awards,
        string SergeantReport,
        string InjuryReport,
        IReadOnlyList<ValueTuple<string, string>> CombatRecord);

    public class SoldierDossierService
    {
        public SoldierDossier BuildDossier(PlayerSoldier soldier, IReadOnlyList<string> history = null,
                                           bool richTextInjury = true, Date currentDate = null,
                                           Sector sector = null,
                                           RatingConsumerBindings ratingBindings = null)
        {
            return new SoldierDossier(
                BuildSoldierData(soldier, currentDate, sector),
                history ?? soldier.SoldierEvents.Select(e => e.Render()).ToList(),
                BuildAwardLines(soldier),
                BuildSergeantReport(soldier, ratingBindings),
                GenerateSoldierInjurySummary(soldier, richTextInjury),
                BuildCombatRecord(soldier));
        }

        // The Battle History card surfaces aggregate combat stats rather than the
        // chronological event log (which now lives under the Service Record card).
        public IReadOnlyList<ValueTuple<string, string>> BuildCombatRecord(PlayerSoldier soldier)
        {
            int operations = soldier.SoldierEvents
                .Count(e => e.Type == SoldierEventType.BattleParticipation);
            int enemiesSlain = soldier.FactionCasualtyCountMap.Values.Sum(count => (int)count);
            int rangedKills = soldier.RangedWeaponCasualtyCountMap.Values.Sum(count => (int)count);
            int meleeKills = soldier.MeleeWeaponCasualtyCountMap.Values.Sum(count => (int)count);

            return
            [
                new("Operations", operations.ToString()),
                new("Enemies Slain", enemiesSlain.ToString()),
                new("Ranged Kills", rangedKills.ToString()),
                new("Melee Kills", meleeKills.ToString())
            ];
        }

        public IReadOnlyList<ValueTuple<string, string>> BuildSoldierData(PlayerSoldier soldier,
                                                                     Date currentDate = null, Sector sector = null)
        {
            List<ValueTuple<string, string>> soldierData =
            [
                new("Time in Service", FormatDurationSince(GetEnlistmentDate(soldier), currentDate)),
                new("Time in Rank", FormatTimeSinceLastEvent(soldier, SoldierEventType.Promotion, currentDate)),
                new("Time in Squad", FormatTimeSinceLastEvent(soldier, SoldierEventType.Transfer, currentDate))
            ];

            soldierData.Add(new ValueTuple<string, string>(
                "Location",
                SquadLocationFormatter.Format(soldier.AssignedSquad)));

            return soldierData;
        }

        // The soldier's earliest recorded event (the Founding note for the Chapter Master,
        // AcceptedToTraining for everyone else) marks the start of his service, and doubles
        // as the fallback "since" date for rank and squad when he has never been promoted
        // or transferred out of his original posting.
        private static Date GetEnlistmentDate(PlayerSoldier soldier)
        {
            return soldier.SoldierEvents
                .Select(e => e.Date)
                .OrderBy(d => d)
                .FirstOrDefault();
        }

        // Weeks the marine has served since enlistment. Shared source of truth for the
        // "Time in Service" display and the soldier filter's duration conditions.
        public static int GetWeeksInService(PlayerSoldier soldier, Date currentDate)
        {
            return GetWeeksSince(GetEnlistmentDate(soldier), currentDate);
        }

        // Weeks since the marine's most recent promotion (or enlistment if never promoted).
        public static int GetWeeksInRank(PlayerSoldier soldier, Date currentDate)
        {
            return GetWeeksSince(GetLastMilestoneDate(soldier, SoldierEventType.Promotion), currentDate);
        }

        // Weeks since the marine's most recent squad transfer (or enlistment if never moved).
        public static int GetWeeksInSquad(PlayerSoldier soldier, Date currentDate)
        {
            return GetWeeksSince(GetLastMilestoneDate(soldier, SoldierEventType.Transfer), currentDate);
        }

        // The date the marine last changed rank, or his enlistment date if he never has. Shared
        // with SoldierSeniority, which orders by this date directly instead of by weeks-in-rank
        // so it can rank soldiers in contexts that have no campaign date to measure against.
        public static Date GetLastPromotionDate(PlayerSoldier soldier)
        {
            return GetLastMilestoneDate(soldier, SoldierEventType.Promotion);
        }

        // Time in rank (Promotion) / time in squad (Transfer) is anchored to the most recent
        // milestone of that kind, or to enlistment if the marine has held his current rank /
        // posting since he first joined.
        private static Date GetLastMilestoneDate(PlayerSoldier soldier, SoldierEventType milestone)
        {
            return soldier.SoldierEvents
                .Where(e => e.Type == milestone)
                .Select(e => e.Date)
                .OrderByDescending(d => d)
                .FirstOrDefault()
                ?? GetEnlistmentDate(soldier);
        }

        private static string FormatTimeSinceLastEvent(PlayerSoldier soldier,
                                                       SoldierEventType milestone, Date currentDate)
        {
            return FormatDurationSince(GetLastMilestoneDate(soldier, milestone), currentDate);
        }

        // Returns -1 when either endpoint is unavailable so callers can distinguish
        // "unknown" from a genuine zero-week span.
        private static int GetWeeksSince(Date since, Date currentDate)
        {
            if (since == null || currentDate == null)
            {
                return -1;
            }
            return Math.Max(0, currentDate.GetWeeksDifference(since));
        }

        // Renders the span from `since` to the current campaign date as "N years" (once a
        // full year has elapsed) or "N weeks", tagged with the anchor date. Falls back to
        // "TBD" when either date is unavailable.
        private static string FormatDurationSince(Date since, Date currentDate)
        {
            int weeks = GetWeeksSince(since, currentDate);
            if (weeks < 0)
            {
                return "TBD";
            }

            int years = weeks / 52;
            string duration = years >= 1
                ? $"{years} {(years == 1 ? "year" : "years")}"
                : $"{weeks} {(weeks == 1 ? "week" : "weeks")}";
            return $"{duration} (since {since})";
        }

        // For an award type with multiple tiers, only the marine's most recent / highest
        // level is surfaced (one line per type), so a brother who has earned successive
        // grades of the same honor shows just his current standing rather than every step.
        public IReadOnlyList<string> BuildAwardLines(PlayerSoldier soldier)
        {
            return HighestPerType(soldier)
                .OrderBy(award => award.DateAwarded)
                .Select(award => $"{award.DateAwarded}: {award.Name}")
                .ToList();
        }

        /// <summary>
        /// A brother's standing with gun and blade, highest grade of each, named as the Chapter
        /// names them ("Gold Bolter of the Emperor"). This is how martial ability is surfaced
        /// where a loadout decision needs context: the player is never shown raw skill values,
        /// so an honor is the readable stand-in for one. Empty for a brother with neither.
        /// </summary>
        public IReadOnlyList<string> BuildCombatHonorNames(
            PlayerSoldier soldier,
            AwardFamilyCatalog awardCatalog = null)
        {
            awardCatalog ??= AwardFamilyCatalog.CreateDefault();
            return HighestPerType(soldier)
                .Where(award => awardCatalog.Get(award.AwardFamilyKey).SummaryGroup == "combat")
                .OrderBy(award => awardCatalog.Get(award.AwardFamilyKey).SortOrder)
                .ThenBy(award => award.AwardFamilyKey, StringComparer.Ordinal)
                .Select(award => award.Name)
                .ToList();
        }

        private static IEnumerable<SoldierAward> HighestPerType(PlayerSoldier soldier)
        {
            return (soldier?.SoldierAwards ?? [])
                .GroupBy(award => award.Type)
                .Select(group => group
                    .OrderByDescending(award => award.Level)
                    .ThenByDescending(award => award.DateAwarded)
                    .First());
        }

        public string BuildSergeantReport(
            PlayerSoldier soldier,
            RatingConsumerBindings ratingBindings = null)
        {
            SoldierEvaluation evaluation = soldier.SoldierEvaluationHistory.LastOrDefault();
            if (evaluation == null)
            {
                return "No sergeant evaluation is available for this battle brother.";
            }

            return GetSergeantDescription(
                soldier.Name,
                evaluation,
                soldier.AssignedSquad?.SquadTemplate?.Name ?? "",
                soldier.Template.IsSquadLeader,
                ratingBindings ?? RatingConsumerBindings.CreateDefault());
        }

        public string GenerateSoldierInjurySummary(ISoldier selectedSoldier, bool richText = true)
        {
            string summary = "";
            byte recoveryTime = 0;
            bool needsReplacement = false;
            foreach (HitLocation hl in selectedSoldier.Body.HitLocations)
            {
                if (hl.Wounds.WoundTotal != 0)
                {
                    // Keep the Chapter/Soldier dossier aligned with the Apothecarium and weekly
                    // medical pass. Current replacement eligibility is limited to severed
                    // non-vital locations.
                    if (hl.IsReplacementEligible)
                    {
                        needsReplacement = true;
                    }
                    byte woundTime = hl.Wounds.RecoveryTimeLeft();
                    if (woundTime > recoveryTime)
                    {
                        recoveryTime = woundTime;
                    }
                    summary += hl.ToString() + "\n";
                }
            }
            if (needsReplacement)
            {
                summary += "Requires replacement treatment before being fully fit for duty\n";
            }
            else if (recoveryTime > 0)
            {
                summary += "Requires " + recoveryTime + " weeks to be fully fit for duty\n";
            }
            else
            {
                summary += "Fully fit and ready to serve the Emperor\n";
            }

            return richText ? summary : StripRichTextTags(summary);
        }

        private static string GetSergeantDescription(
            string name,
            SoldierEvaluation evaluation,
            string squadType,
            bool isSquadLeader,
            RatingConsumerBindings ratingBindings)
        {
            float leadershipRating = ratingBindings.Get(
                evaluation, RatingConsumerRole.CommandLeadership);
            float rangedRating = ratingBindings.Get(
                evaluation, RatingConsumerRole.RangedCombat);
            float meleeRating = ratingBindings.Get(
                evaluation, RatingConsumerRole.MeleeCombat);
            if (isSquadLeader)
            {
                if (leadershipRating > 55)
                {
                    return name + " leads his squad with distinction, and should be considered for greater command responsibilities.";
                }
                else
                {
                    return name + " is capably fulfilling his duties as a squad leader.";
                }
            }

            int maxLevel = 0;
            if (rangedRating > 105 && meleeRating < 90)
            {
                maxLevel = 1;
            }
            else if (rangedRating > 105 && meleeRating > 90)
            {
                if (rangedRating > 110 && meleeRating > 95)
                {
                    if (leadershipRating > 55)
                    {
                        maxLevel = 4;
                    }
                    else
                    {
                        maxLevel = 3;
                    }
                }
                else
                {
                    maxLevel = 2;
                }
            }
            if ("Scout Squad" == squadType || "Scout HQ Squad" == squadType)
            {
                if (maxLevel > 0)
                {
                    return name + " is ready for his Black Carapace and assignment to a Devastator Squad.";
                }
                else
                {
                    return name + " is not ready to become a Battle Brother, and should acquire more seasoning before taking the Black Carapace.";
                }
            }
            if ("Devastator Squad" == squadType)
            {
                if (maxLevel > 1)
                {
                    return name + " has shown sufficient capabilities to be ready for a spot on an assault squad.";
                }
                else
                {
                    return name + " still has much to learn before being ready for promotion to an assault squad.";
                }
            }
            if ("Assault Squad" == squadType)
            {
                if (maxLevel > 2)
                {
                    return name + " has sufficient skill with both gun and blade to be ready for a posting to a tactical squad.";
                }
                else
                {
                    return name + " is not yet fully comfortable with all forms of combat, and should remain in an assault squad for more seasoning.";
                }
            }
            if ("Tactical Squad" == squadType)
            {
                if (maxLevel > 3)
                {
                    return name + " has shown leadership potential, and should be a candidate for sergeant.";
                }
                else
                {
                    return name + " is performing well in his current role.";
                }
            }
            else
            {
                return "I have no opinion on future assignments for " + name + ".";
            }
        }

        private static string StripRichTextTags(string text)
        {
            return Regex.Replace(text, "<.*?>", "");
        }
    }
}
