using System.Collections.Generic;
namespace OnlyWar.Battles.Abstractions {
    // Ordered worst-first: the debrief roster sorts on this.
    public enum BattleCasualtyDisposition
    {
        Dead,
        // Out of the fight but alive, and carried off the field
        // (Design/Reference/CasualtyRealism.md §2.3). Listed above ReplacementRequired because an
        // incapacitated brother is usually also awaiting a replacement limb, and "he went down"
        // is the more important fact.
        Incapacitated,
        ReplacementRequired,
        Recovering
    }

    public sealed record BattleCasualtyEntry(
        int SoldierId,
        string Name,
        string Rank,
        string Squad,
        string Company,
        BattleCasualtyDisposition Disposition,
        int RecoveryWeeks);

    public sealed record BattleDebriefReport(
        int PlayerDeaths,
        int OpposingDeaths,
        IReadOnlyList<BattleCasualtyEntry> PlayerCasualties,
        // Brothers taken out of this engagement alive. Appended rather than inserted so existing
        // three-argument construction keeps compiling and keeps meaning what it meant.
        int PlayerIncapacitated = 0);

}