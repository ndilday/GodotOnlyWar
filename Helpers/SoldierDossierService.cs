using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OnlyWar.Helpers
{
    public sealed record SoldierDossier(
        IReadOnlyList<Tuple<string, string>> Data,
        IReadOnlyList<string> History,
        IReadOnlyList<string> Awards,
        string SergeantReport,
        string InjuryReport,
        IReadOnlyList<Tuple<string, string>> CombatRecord);

    public class SoldierDossierService
    {
        public SoldierDossier BuildDossier(PlayerSoldier soldier, IReadOnlyList<string> history = null,
                                           bool richTextInjury = true, Date currentDate = null, Sector sector = null)
        {
            return new SoldierDossier(
                BuildSoldierData(soldier, currentDate, sector),
                history ?? soldier.SoldierHistory.ToList(),
                BuildAwardLines(soldier),
                BuildSergeantReport(soldier),
                GenerateSoldierInjurySummary(soldier, richTextInjury),
                BuildCombatRecord(soldier));
        }

        // The Battle History card surfaces aggregate combat stats rather than the
        // chronological event log (which now lives under the Service Record card).
        public IReadOnlyList<Tuple<string, string>> BuildCombatRecord(PlayerSoldier soldier)
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

        public IReadOnlyList<Tuple<string, string>> BuildSoldierData(PlayerSoldier soldier,
                                                                     Date currentDate = null, Sector sector = null)
        {
            List<Tuple<string, string>> soldierData =
            [
                new("Name", soldier.Name),
                new("Time in Service", FormatTimeInService(soldier, currentDate))
            ];

            if (soldier.AssignedSquad?.BoardedLocation != null)
            {
                soldierData.Add(new Tuple<string, string>("Location", $"Aboard {soldier.AssignedSquad.BoardedLocation.Name}"));
            }
            else if (soldier.AssignedSquad?.CurrentRegion != null)
            {
                Region region = soldier.AssignedSquad.CurrentRegion;
                string subsectorName = ResolveSubsectorName(sector, region.Planet);
                soldierData.Add(new Tuple<string, string>(
                    "Location",
                    $"Region {region.Id}, {region.Planet.Name}, {subsectorName}"));
            }
            else
            {
                soldierData.Add(new Tuple<string, string>("Location", "Unknown"));
            }

            return soldierData;
        }

        // Service length measured from the soldier's earliest recorded event (the Founding
        // note for the Chapter Master, AcceptedToTraining for everyone else) to the current
        // campaign date. Falls back to "TBD" when either date is unavailable.
        private static string FormatTimeInService(PlayerSoldier soldier, Date currentDate)
        {
            Date enlistment = soldier.SoldierEvents
                .Select(e => e.Date)
                .OrderBy(d => d)
                .FirstOrDefault();
            if (enlistment == null || currentDate == null)
            {
                return "TBD";
            }

            int weeks = Math.Max(0, currentDate.GetWeeksDifference(enlistment));
            int years = weeks / 52;
            string duration = years >= 1
                ? $"{years} {(years == 1 ? "year" : "years")}' service"
                : $"{weeks} {(weeks == 1 ? "week" : "weeks")}' service";
            return $"{duration} (since {enlistment})";
        }

        // A Planet carries no back-reference to its Subsector, so we resolve it by scanning
        // the sector's subsectors for the one that owns the world.
        private static string ResolveSubsectorName(Sector sector, Planet planet)
        {
            Subsector subsector = sector?.Subsectors
                .FirstOrDefault(s => s.Planets.Contains(planet));
            return subsector?.Name ?? "Subsector TBD";
        }

        // For an award type with multiple tiers, only the marine's most recent / highest
        // level is surfaced (one line per type), so a brother who has earned successive
        // grades of the same honor shows just his current standing rather than every step.
        public IReadOnlyList<string> BuildAwardLines(PlayerSoldier soldier)
        {
            return soldier.SoldierAwards
                .GroupBy(award => award.Type)
                .Select(group => group
                    .OrderByDescending(award => award.Level)
                    .ThenByDescending(award => award.DateAwarded)
                    .First())
                .OrderBy(award => award.DateAwarded)
                .Select(award => $"{award.DateAwarded}: {award.Name}")
                .ToList();
        }

        public string BuildSergeantReport(PlayerSoldier soldier)
        {
            SoldierEvaluation evaluation = soldier.SoldierEvaluationHistory.LastOrDefault();
            if (evaluation == null)
            {
                return "No sergeant evaluation is available for this battle brother.";
            }

            return GetSergeantDescription(
                soldier.Name,
                evaluation,
                soldier.AssignedSquad?.SquadTemplate?.Name ?? "");
        }

        public string GenerateSoldierInjurySummary(ISoldier selectedSoldier, bool richText = true)
        {
            string summary = selectedSoldier.Name + "\n";
            byte recoveryTime = 0;
            bool isSevered = false;
            foreach (HitLocation hl in selectedSoldier.Body.HitLocations)
            {
                if (hl.Wounds.WoundTotal != 0)
                {
                    if (hl.IsSevered)
                    {
                        isSevered = true;
                    }
                    byte woundTime = hl.Wounds.RecoveryTimeLeft();
                    if (woundTime > recoveryTime)
                    {
                        recoveryTime = woundTime;
                    }
                    summary += hl.ToString() + "\n";
                }
            }
            if (isSevered)
            {
                summary += selectedSoldier.Name +
                    " will be unable to perform field duties until receiving cybernetic replacements\n";
            }
            else if (recoveryTime > 0)
            {
                summary += selectedSoldier.Name +
                    " requires " + recoveryTime + " weeks to be fully fit for duty\n";
            }
            else
            {
                summary += selectedSoldier.Name +
                    " is fully fit and ready to serve the Emperor\n";
            }

            return richText ? summary : StripRichTextTags(summary);
        }

        private static string GetSergeantDescription(string name, SoldierEvaluation evaluation, string squadType)
        {
            int maxLevel = 0;
            if (evaluation.RangedRating > 105 && evaluation.MeleeRating < 90)
            {
                maxLevel = 1;
            }
            else if (evaluation.RangedRating > 105 && evaluation.MeleeRating > 90)
            {
                if (evaluation.RangedRating > 110 && evaluation.MeleeRating > 95)
                {
                    if (evaluation.LeadershipRating > 55)
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
