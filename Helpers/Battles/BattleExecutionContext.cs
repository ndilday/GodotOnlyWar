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

        internal BattleExecutionContext(
            GameRulesData rules,
            IRNG random,
            BattleAftermathDependencies aftermath)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Random = random ?? throw new ArgumentNullException(nameof(random));
            Aftermath = aftermath ?? throw new ArgumentNullException(nameof(aftermath));

            if (!ReferenceEquals(random, aftermath.Random))
            {
                throw new ArgumentException(
                    "Battle resolution and aftermath must share the same random stream.",
                    nameof(aftermath));
            }
        }
    }
}
