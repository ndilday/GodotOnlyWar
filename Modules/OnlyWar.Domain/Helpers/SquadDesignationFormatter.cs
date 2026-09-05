using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OnlyWar.Helpers
{
    public static class SquadDesignationFormatter
    {
        private static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["First"] = 1, ["Second"] = 2, ["Third"] = 3, ["Fourth"] = 4,
            ["Fifth"] = 5, ["Sixth"] = 6, ["Seventh"] = 7, ["Eighth"] = 8,
            ["Ninth"] = 9, ["Tenth"] = 10
        };

        public static bool IsNumberedLineFormation(Squad squad)
        {
            SquadTypes type = squad?.SquadTemplate?.SquadType ?? SquadTypes.None;
            SquadTypes excluded = SquadTypes.HQ | SquadTypes.Scout | SquadTypes.Administrative;
            return squad?.Faction?.IsPlayerFaction == true && (type & excluded) == 0;
        }

        public static string Format(Squad squad)
        {
            if (squad == null) throw new ArgumentNullException(nameof(squad));
            if (!squad.FormationOrdinal.HasValue || !IsNumberedLineFormation(squad))
            {
                return squad.Name ?? squad.SquadTemplate?.Name ?? "Formation";
            }

            string role = NormalizeRoleName(squad.SquadTemplate?.Name);
            return $"{ToRoman(squad.FormationOrdinal.Value)} {role} Squad, {FormatCompanySuffix(squad.ParentUnit)}";
        }

        public static string FormatScoutSquad(Squad squad)
        {
            if (squad == null) throw new ArgumentNullException(nameof(squad));
            string fallback = squad.SquadTemplate?.Name ?? "Scout Squad";
            string leaderName = squad.SquadLeader?.Name;
            if (string.IsNullOrWhiteSpace(leaderName))
            {
                return fallback;
            }

            string surname = leaderName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();
            return string.IsNullOrWhiteSpace(surname) ? fallback : $"{surname} Squad";
        }

        public static string ToRoman(int value)
        {
            if (value <= 0 || value > 3999)
                throw new ArgumentOutOfRangeException(nameof(value), "Roman ordinals must be between 1 and 3999.");
            (int Value, string Token)[] tokens =
            [
                (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
                (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
                (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
            ];
            string result = string.Empty;
            foreach ((int tokenValue, string token) in tokens)
            {
                while (value >= tokenValue)
                {
                    result += token;
                    value -= tokenValue;
                }
            }
            return result;
        }

        private static string NormalizeRoleName(string templateName)
        {
            string name = string.IsNullOrWhiteSpace(templateName) ? "Line" : templateName.Trim();
            return Regex.Replace(name, @"\s+Squad$", string.Empty, RegexOptions.IgnoreCase);
        }

        private static string FormatCompanySuffix(Unit company)
        {
            string name = company?.Name ?? "Company";
            return TryGetCompanyNumber(name, out int number)
                ? $"{number} Co."
                : Regex.Replace(name, @"\s+Company$", string.Empty, RegexOptions.IgnoreCase) + " Co.";
        }

        internal static bool TryGetCompanyNumber(string name, out int companyNumber)
        {
            companyNumber = 0;
            if (string.IsNullOrWhiteSpace(name)) return false;

            Match digit = Regex.Match(name, @"\b(\d+)(?:st|nd|rd|th)?\b", RegexOptions.IgnoreCase);
            if (digit.Success && int.TryParse(digit.Groups[1].Value, out companyNumber))
            {
                return true;
            }

            string firstWord = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return firstWord != null && NumberWords.TryGetValue(firstWord, out companyNumber);
        }
    }

    public static class FormationOrdinalAllocator
    {
        public static int GetNextOrdinal(Unit company)
        {
            if (company == null) throw new ArgumentNullException(nameof(company));
            return GetNextOrdinal(company, null);
        }

        public static int GetNextOrdinal(Unit company, SquadTemplate squadTemplate)
        {
            if (company == null) throw new ArgumentNullException(nameof(company));
            HashSet<int> used = company.Squads
                .Where(SquadDesignationFormatter.IsNumberedLineFormation)
                .Where(squad => squad.FormationOrdinal.HasValue)
                .Select(squad => squad.FormationOrdinal.Value)
                .ToHashSet();

            // Codex battle companies keep the Codex Astartes role blocks even when a
            // founding cohort cannot seed every tactical squad: I-VIII tactical, IX
            // assault, and X devastator.
            if (TryGetBattleCompanyNumber(company, out _))
            {
                string templateName = squadTemplate?.Name;
                (int First, int Last)? canonicalRange =
                    string.Equals(templateName, "Tactical Squad", StringComparison.OrdinalIgnoreCase)
                        ? (1, 8)
                        : string.Equals(templateName, "Assault Squad", StringComparison.OrdinalIgnoreCase)
                            ? (9, 9)
                            : string.Equals(templateName, "Devastator Squad", StringComparison.OrdinalIgnoreCase)
                                ? (10, 10)
                                : null;
                if (canonicalRange.HasValue)
                {
                    for (int candidateOrdinal = canonicalRange.Value.First;
                         candidateOrdinal <= canonicalRange.Value.Last;
                         candidateOrdinal++)
                    {
                        if (!used.Contains(candidateOrdinal))
                        {
                            return candidateOrdinal;
                        }
                    }
                }
            }

            int ordinal = 1;
            while (used.Contains(ordinal)) ordinal++;
            return ordinal;
        }

        private static bool TryGetBattleCompanyNumber(Unit company, out int companyNumber)
        {
            companyNumber = 0;
            return SquadDesignationFormatter.TryGetCompanyNumber(company?.Name, out companyNumber)
                && companyNumber is >= 2 and <= 5;
        }
    }
}
