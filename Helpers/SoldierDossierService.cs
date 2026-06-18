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
        string InjuryReport);

    public class SoldierDossierService
    {
        public SoldierDossier BuildDossier(PlayerSoldier soldier, IReadOnlyList<string> history = null, bool richTextInjury = true)
        {
            return new SoldierDossier(
                BuildSoldierData(soldier),
                history ?? soldier.SoldierHistory.ToList(),
                BuildAwardLines(soldier),
                BuildSergeantReport(soldier),
                GenerateSoldierInjurySummary(soldier, richTextInjury));
        }

        public IReadOnlyList<Tuple<string, string>> BuildSoldierData(PlayerSoldier soldier)
        {
            List<Tuple<string, string>> soldierData =
            [
                new("Name", soldier.Name),
                new("Time in Service", "TBD")
            ];

            if (soldier.AssignedSquad?.BoardedLocation != null)
            {
                soldierData.Add(new Tuple<string, string>("Location", $"Aboard {soldier.AssignedSquad.BoardedLocation.Name}"));
            }
            else if (soldier.AssignedSquad?.CurrentRegion != null)
            {
                soldierData.Add(new Tuple<string, string>(
                    "Location",
                    $"Region {soldier.AssignedSquad.CurrentRegion.Id}, {soldier.AssignedSquad.CurrentRegion.Planet.Name}, Subsector TBD"));
            }
            else
            {
                soldierData.Add(new Tuple<string, string>("Location", "Unknown"));
            }

            return soldierData;
        }

        public IReadOnlyList<string> BuildAwardLines(PlayerSoldier soldier)
        {
            return soldier.SoldierAwards
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
