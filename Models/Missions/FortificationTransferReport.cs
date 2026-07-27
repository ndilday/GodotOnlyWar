using OnlyWar.Models.Planets;

namespace OnlyWar.Models.Missions
{
    /// <summary>
    /// Records that a faction's defensive works in a region changed hands to an ally, because the
    /// faction that built them no longer has anyone there to man them.
    /// </summary>
    /// <remarks>
    /// The player needs to be told: a squad marching out of a region it spent weeks fortifying has
    /// not wasted that effort, and the works have not vanished - the garrison it hands them to
    /// still holds the ground. Since the merge is level-preserving for the side, nothing is lost in
    /// the handover, only in who is credited with it.
    /// </remarks>
    public sealed class FortificationTransferReport
    {
        public Region Region { get; }
        public Faction From { get; }
        public Faction To { get; }
        // The side's position in the region after the handover - unchanged by it, which is exactly
        // the reassurance the report exists to give.
        public double SharedEntrenchment { get; }

        public bool IsPlayerHandover => From != null && From.IsPlayerFaction;

        public FortificationTransferReport(
            Region region,
            Faction from,
            Faction to,
            double sharedEntrenchment)
        {
            Region = region;
            From = from;
            To = to;
            SharedEntrenchment = sharedEntrenchment;
        }
    }
}
