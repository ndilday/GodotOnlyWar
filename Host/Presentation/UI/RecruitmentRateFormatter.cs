using System;
using System.Globalization;

namespace OnlyWar.Host.Presentation.UI;

/// <summary>
/// Formats detached recruitment forecast values for Godot controls. Recruitment rules stay in
/// Application; this helper owns only the presentation wording.
/// </summary>
public static class RecruitmentRatePresentation
{
    public static string FormatWeekly(double rate)
    {
        if (double.IsNaN(rate) || double.IsInfinity(rate) || rate < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate));
        }

        if (rate == 0)
        {
            return "0 per week";
        }

        if (rate < 1)
        {
            long weeks = Math.Max(2, (long)Math.Ceiling(1 / rate));
            return $"approximately 1 every {weeks} weeks";
        }

        return $"{rate.ToString("0.##", CultureInfo.InvariantCulture)} per week";
    }
}
