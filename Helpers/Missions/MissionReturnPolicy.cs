using OnlyWar.Models.Missions;

namespace OnlyWar.Helpers.Missions
{
    /// <summary>
    /// What a force does with the ground when its objective is resolved.
    /// </summary>
    public enum MissionReturnPolicy
    {
        /// <summary>Takes and holds the ground. Never exfiltrates.</summary>
        Hold,

        /// <summary>Engages and comes home. Exfiltrates when operating away from ground it holds.</summary>
        Return,

        /// <summary>Never leaves its own region. Neither infiltrates nor exfiltrates.</summary>
        Static
    }

    /// <summary>
    /// Whether a mission comes home, holds what it took, or never left.
    /// </summary>
    /// <remarks>
    /// This replaces a purely geometric test. <c>MissionContext.MustExfiltrate</c> used to compare the
    /// target region against the force's current region and nothing else, which was correct only by
    /// accident: the assault path never consulted it, because PrepareAssaultMissionStep had no
    /// exfiltration step at all. Once every mission shares one shape (infiltrate → attempt →
    /// exfiltrate), a successful assault would have tried to withdraw from ground it had just taken.
    ///
    /// The Hold/Return split is also the substantive difference between an advance and a raid: a
    /// lightning raid or hit-and-run exists to engage the enemy and withdraw, an advance exists to take
    /// the ground. Before this it was expressed only as which steps each mission type happened to run.
    /// </remarks>
    public static class MissionReturnPolicies
    {
        public static MissionReturnPolicy GetPolicy(MissionType missionType) => missionType switch
        {
            // Takes ground and stays on it, so it gets the full week of attempts rather than
            // surrendering the last day to a trip home.
            MissionType.Advance
                or MissionType.EstablishAirhead
                or MissionType.DeepStrike => MissionReturnPolicy.Hold,

            // Operates from, and remains in, its own region. A diversion demonstrates across a border;
            // patrol, defence, construction, feeding and a show of force are all postures held at
            // home. (Feed never reaches the mission-step machinery at all - it is squad-less - but it
            // is classified here so the mapping stays total rather than defaulting to Return.)
            MissionType.Diversion
                or MissionType.Feed
                or MissionType.Patrol
                or MissionType.DefenseInDepth
                or MissionType.LastStand
                or MissionType.Fortify
                or MissionType.Construction
                or MissionType.Training
                or MissionType.ShowOfForce => MissionReturnPolicy.Static,

            // Everything else goes in, does the job, and comes back: recon, sabotage, assassination,
            // ambush, extermination, and the raid family.
            _ => MissionReturnPolicy.Return
        };
    }
}
