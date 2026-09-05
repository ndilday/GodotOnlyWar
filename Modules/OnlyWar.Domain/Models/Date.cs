using System;
namespace OnlyWar.Models
{
    [Serializable]
    public class Date : IComparable
    {
        public int Millenium;
        public int Year;
        public int Week;
        public Date(int millenium, int year, int week)
        {
            Millenium = millenium;
            Year = year;
            Week = week;
        }

        public Date(int weeks)
        {
            // Jan 1, 1 AD is the start of Week 1
            int years = weeks / 52;
            Week = (weeks % 52) + 1;
            Millenium = (years / 1000) + 1;
            Year = (years % 1000) + 1;
            // The campaign calendar represents the final year of a millennium as
            // year 000 of the following millennium (e.g. 000.M42), matching IncrementWeek.
            if (Year == 1000)
            {
                Millenium++;
                Year = 0;
            }
        }

        // Save tables store GetTotalWeeks(), whose first valid week is 1. The legacy
        // single-int constructor accepts a zero-based week offset, so persistence must
        // cross that boundary explicitly to avoid shifting every restored date by one week.
        public static Date FromTotalWeeks(int totalWeeks)
        {
            if (totalWeeks < 1) throw new ArgumentOutOfRangeException(nameof(totalWeeks));
            return new Date(totalWeeks - 1);
        }

        public void IncrementWeek()
        {
            if(Week == 52)
            {
                Week = 1;
                if (Year == 999)
                {
                    Year = 0;
                    Millenium++;
                }
                else
                {
                    Year++;
                }
            }
            else
            {
                Week++;
            }
        }
        public override string ToString()
        {
            return Week.ToString() + "." + Year.ToString() + ".M" + Millenium.ToString();
        }

        public bool IsBetweenInclusive(Date earlierDate, Date laterDate)
        {
            return IsAfterOrEqual(earlierDate) && IsBeforeOrEqual(laterDate);
        }

        public bool IsBeforeOrEqual(Date otherDate)
        {
            if(Millenium > otherDate.Millenium
                || (Millenium == otherDate.Millenium && Year > otherDate.Year)
                || (Millenium == otherDate.Millenium && Year == otherDate.Year && Week > otherDate.Week))
            {
                return false;
            }
            return true;
        }

        public bool IsAfterOrEqual(Date otherDate)
        {
            if (Millenium < otherDate.Millenium
                || (Millenium == otherDate.Millenium && Year < otherDate.Year)
                || (Millenium == otherDate.Millenium && Year == otherDate.Year && Week < otherDate.Week))
            {
                return false;
            }
            return true;
        }

        public int CompareTo(object obj)
        {
            if (obj == null) return 1;
            if (!(obj is Date otherDate))
            {
                throw new ArgumentException("Object is not a Date");
            }
            if (Equals(otherDate)) return 0;
            if (this.IsBeforeOrEqual(otherDate)) return -1;
            return 1;
        }

        public override bool Equals(object obj)
        {
            if (obj == null) return false;
            if (!(obj is Date otherDate))
            {
                return false;
            }
            return Millenium == otherDate.Millenium
                && Year == otherDate.Year
                && Week == otherDate.Week;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Millenium, Year, Week);
        }

        public int GetWeeksDifference(Date otherDate)
        {
            return ((Millenium - otherDate.Millenium) * 52000)
                + ((Year - otherDate.Year) * 52)
                + (Week - otherDate.Week);
        }

        public int GetTotalWeeks()
        {
            return ((Millenium - 1) * 52000) + ((Year - 1) * 52) + Week;
        }
    }
}
