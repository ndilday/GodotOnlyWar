using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Models;
using System;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Immutable dependencies for one tactical battle. The mission layer creates this from its
    /// session-scoped inputs; battle helpers receive only the rules, random stream, and explicit
    /// campaign-effects boundary they need rather than reaching back into GameDataSingleton.
    /// </summary>
    public sealed class BattleExecutionContext
    {
        internal GameRulesData Rules { get; }
        public IRNG Random { get; }
        internal BattleAftermathDependencies Aftermath { get; }
        internal int MaxPlanningDegreeOfParallelism { get; }

        /// <summary>
        /// Whether an INERT battle -- neither side taking casualties, neither side moving -- should
        /// be raised as an exception rather than merely logged and disengaged.
        ///
        /// <para>OFF FOR THE GAME, ON FOR TESTS, and the asymmetry is the point. An inert battle is
        /// always an engine bug (see <c>BattleTurnResolver.MaxBattleTurns</c>, which has said so in
        /// a comment for as long as it has existed), but it is a bug the resolver already SURVIVES:
        /// the forced disengagement is a real, sane outcome. Throwing in a player's session would
        /// convert a survivable emergent situation into a lost campaign turn, and it would do so
        /// over content -- weapon ranges, soldier skills -- that a mod can legitimately change.</para>
        ///
        /// <para>Logging alone, though, is not a signal. Seven battles per run of one mission test
        /// had been hitting the turn cap silently, because a Warn goes to a sink nothing reads in a
        /// test run. Failing the test is what makes it noticed.</para>
        /// </summary>
        internal bool ThrowOnInertBattle { get; }

        internal BattleExecutionContext(
            GameRulesData rules,
            IRNG random,
            BattleAftermathDependencies aftermath,
            int maxPlanningDegreeOfParallelism = 0,
            bool throwOnInertBattle = false)
        {
            ThrowOnInertBattle = throwOnInertBattle;
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Random = random ?? throw new ArgumentNullException(nameof(random));
            Aftermath = aftermath ?? throw new ArgumentNullException(nameof(aftermath));
            MaxPlanningDegreeOfParallelism = maxPlanningDegreeOfParallelism <= 0
                ? Math.Max(1, Environment.ProcessorCount)
                : maxPlanningDegreeOfParallelism;

            if (!ReferenceEquals(random, aftermath.Random))
            {
                throw new ArgumentException(
                    "Battle resolution and aftermath must share the same random stream.",
                    nameof(aftermath));
            }
        }
    }
}
