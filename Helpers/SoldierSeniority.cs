using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    // The single definition of "who is senior to whom": rank first, then subrank (which separates
    // roles sharing a Rank — a Veteran Sergeant outranks a plain Sergeant at Rank 5), then tenure,
    // so the longest-held brother within a rank leads his equals.
    //
    // Tenure is compared as the *date* of a soldier's most recent promotion rather than as weeks
    // in rank. The two orderings are equivalent — weeks-in-rank descending is promotion-date
    // ascending, since every soldier is measured against the same "today" — but the date form
    // needs no campaign date passed in. That matters because mission resolution has no Date
    // available at all (MissionExecutionContext carries State/Rules/Random/Battle/EntityIds and
    // nothing else), and command seniority has to be decidable there too.
    //
    // Rank is only comparable WITHIN a faction. The non-Imperial templates use it as a power
    // scale rather than a chain of command (Genestealer 20, Hive Tyrant 100), so ordering a
    // mixed-faction set by seniority is meaningless. Every current caller passes a single
    // faction's soldiers.
    public static class SoldierSeniority
    {
        // Sorts most senior first. Callers may append further ThenBy clauses to break ties that
        // rank, subrank, and tenure leave equal (which is common — brothers promoted in the same
        // week share a tenure date).
        public static IOrderedEnumerable<ISoldier> OrderBySeniority(IEnumerable<ISoldier> soldiers)
        {
            return OrderBySeniority(soldiers, soldier => soldier);
        }

        // Selector overload, so wrappers that hold an ISoldier rather than being one (BattleSoldier)
        // can be ordered without unwrapping and re-pairing them.
        public static IOrderedEnumerable<T> OrderBySeniority<T>(
            IEnumerable<T> items, Func<T, ISoldier> soldierSelector)
        {
            return items
                .OrderByDescending(item => soldierSelector(item).Template.Rank)
                .ThenByDescending(item => soldierSelector(item).Template.Subrank)
                .ThenBy(item => GetTenureKey(soldierSelector(item)));
        }

        // Ascending sort key for time in rank: the earlier a soldier last changed rank, the longer
        // he has held it, and the more senior he is among his equals. Expressed as total weeks so
        // "unknown" can be int.MaxValue and sort last — a plain Date key would sort nulls *first*
        // under the default comparer and silently promote soldiers with no service record.
        //
        // Only PlayerSoldier carries a promotion history, so NPC soldiers all land in that
        // unknown bucket and are separated by whatever tiebreak the caller appends.
        public static int GetTenureKey(ISoldier soldier)
        {
            Date since = GetTenureDate(soldier);
            return since == null ? int.MaxValue : since.GetTotalWeeks();
        }

        // The soldier's most recent promotion, or his enlistment if he has never been promoted;
        // null for anyone without a service record to read.
        public static Date GetTenureDate(ISoldier soldier)
        {
            return soldier is PlayerSoldier player
                ? SoldierDossierService.GetLastPromotionDate(player)
                : null;
        }
    }
}
