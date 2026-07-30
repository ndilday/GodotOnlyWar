using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Missions
{
    /// <summary>
    /// A day of sweeping ground the force already holds, looking for anything moving through it.
    /// </summary>
    /// <remarks>
    /// Patrol used to run no mission at all - it was a bare `continue` in
    /// MissionTurnProcessor.ProcessCombatMissions, alongside Defense. That is why patrolling granted no
    /// field experience despite being a week of work, and why it had nothing to report.
    ///
    /// This step is the week's WORK. It is deliberately not the same thing as the detection check that
    /// decides whether a patrol joins the defence of its region: that question - "were you looking the
    /// right way when this particular attack came in?" - is asked at the moment of contact by
    /// PrepareAssaultMissionStep, because it depends on the size of the force arriving and on how much
    /// attention a feint had drawn away by then. Asking it here instead would mean answering it before
    /// the day's attacks existed.
    ///
    /// What the sweep does contribute, beyond experience: the squads are in the region's LandedSquads and
    /// on a Patrol order, so they already feed RegionFaction.GetPatrolStrength and therefore the
    /// search-effort term every intruder's stealth check faces (MissionStealthDifficulty), and they
    /// already weight Region.SelectSpotter so that an intrusion caught here is the player's sighting
    /// rather than nobody's. Neither of those needed new code; they only needed patrol to stop being a
    /// no-op so the order was worth issuing.
    /// </remarks>
    public class PatrolSweepMissionStep : IMissionStep
    {
        // Baseline difficulty of covering your own ground for a day, matching the other leader-level
        // mission checks so a patrol is neither trivially nor unusually hard to perform well.
        private const float SweepDifficulty = 10.0f;

        // How much a day's sweep suffers per point of attention drawn elsewhere. A demonstration on the
        // border does not only make the screen worse at catching infiltrators (which it does through
        // MissionStealthDifficulty); it makes the screen worse at its own job, which is what the player
        // running the feint is paying for.
        private const float CommittedAttentionPenalty = 1.0f;

        public string Description => "Patrol Sweep";

        public bool ConsumesDay => true;

        public MissionStepResult ExecuteMissionStep(
            MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
        {
            MissionContext context = execution.State;
            RegionFaction patrolled = context.Order.Mission.RegionFaction;

            // A patrol is Static (MissionReturnPolicy), so it never exfiltrates and works the full week.
            if (context.OperatingDaysSpent)
            {
                return MissionStepResult.Complete;
            }

            context.DaysElapsed++;

            float difficulty = SweepDifficulty
                + patrolled.CommittedAttention * CommittedAttentionPenalty
                // Ground covered is what boldness buys, so the sweep sits on aggression's EFFECT axis: a
                // Cautious patrol keeps close to its own positions and turns over less ground.
                + MissionAggressionModifiers.EffectDifficulty(context.Order.LevelOfAggression);

            BaseSkill tactics = execution.Rules.Tactics;
            float margin = new LeaderMissionTest(tactics, difficulty)
                .RunMissionCheck(context.MissionSquads, execution.Random);
            if (margin > 0f)
            {
                context.Impact += margin;
            }

            context.AddLog(margin > 0f
                ? $"Day {context.DaysElapsed}: Force sweeps {patrolled.Region.Name}; the ground is covered."
                : $"Day {context.DaysElapsed}: Force sweeps {patrolled.Region.Name}; the patrol is thin and much is missed.");
            GameLog.Trace(() =>
                $"Patrol sweep {patrolled.PlanetFaction.Faction.Name} {patrolled.Region.Name} "
                + $"day {context.DaysElapsed}: difficulty={difficulty:F2} "
                + $"(committedAttention={patrolled.CommittedAttention:F2}), margin={margin:F2}");

            return MissionStepResult.Continue(this, marginOfSuccess, resumeStep);
        }
    }
}
