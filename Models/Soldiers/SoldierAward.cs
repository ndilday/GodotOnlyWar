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

        public SoldierAward(Date dateAwarded, string name)
        {
            DateAwarded = dateAwarded;
            Name = name;
        }
    }
}
