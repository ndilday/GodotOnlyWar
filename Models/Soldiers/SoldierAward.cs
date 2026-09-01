using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Models.Soldiers
{
    public class SoldierAward
    {
        public Date DateAwarded { get; }
        public string Name { get; }
        public string Type { get; }
        // Type remains for save-format compatibility; new content treats it as
        // the stable award-family key rather than a closed enum.
        public string AwardFamilyKey => Type;
        public ushort Level { get; }

        public SoldierAward(Date dateAwarded, string name, string type, ushort level)
        {
            // Date is the mutable campaign clock. Records must retain the date on which
            // they were created rather than following that clock as turns advance.
            DateAwarded = CopyDate(dateAwarded);
            Name = name;
            Type = type;
            Level = level;
        }

        private static Date CopyDate(Date date) =>
            date == null ? null : new Date(date.Millenium, date.Year, date.Week);
    }
}
