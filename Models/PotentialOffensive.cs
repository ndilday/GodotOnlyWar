using OnlyWar.Models.Planets;
using System.Collections.Generic;

namespace OnlyWar.Models
{
    internal class PotentialOffensive
    {
        public Region TargetRegion { get; set; }
        public RegionFaction TargetFaction { get; set; }
        public List<RegionFaction> AttackingFactions { get; set; } = new List<RegionFaction>();
        public long CombinedAttackingForce { get; set; }
    }
}
