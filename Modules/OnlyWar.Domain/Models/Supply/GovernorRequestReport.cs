namespace OnlyWar.Models.Supply
{
    public enum GovernorRequestReportKind
    {
        /// <summary>A governor petitioned the Chapter this turn.</summary>
        Arrived = 0,
        /// <summary>The commitment was met and a pledge was raised against it.</summary>
        Fulfilled = 1,
        /// <summary>The deadline passed, the threat resolved without the Chapter, or the
        /// petitioning governor died with the request still open.</summary>
        Failed = 2
    }

    /// <summary>
    /// One governor-request lifecycle event, carried out of the turn so the end-of-turn report
    /// can mention it. Requests were previously created, fulfilled and failed in total silence -
    /// the player's only clue was a standing opinion penalty with no stated cause.
    /// Mirrors ConstructionProgressReport/FortificationTransferReport: a plain record produced by
    /// the simulation and formatted by the UI, with no Godot dependency of its own.
    /// </summary>
    public sealed class GovernorRequestReport
    {
        public GovernorRequestReportKind Kind { get; }
        public IRequest Request { get; }
        /// <summary>
        /// Set only for <see cref="GovernorRequestReportKind.Failed"/>, explaining which failure
        /// path was taken so the report can say why rather than merely that.
        /// </summary>
        public string FailureReason { get; }

        public GovernorRequestReport(
            GovernorRequestReportKind kind,
            IRequest request,
            string failureReason = null)
        {
            Kind = kind;
            Request = request;
            FailureReason = failureReason;
        }
    }
}
