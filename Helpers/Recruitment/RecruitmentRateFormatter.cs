using System;
using System.Globalization;

namespace OnlyWar.Helpers.Recruitment
{
    public static class RecruitmentRateFormatter
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
}
