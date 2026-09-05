using System;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Raised when a battle stops progressing entirely -- no casualties on either side, no change
    /// in separation -- for <c>BattleTurnResolver.InertTurnThreshold</c> consecutive turns.
    ///
    /// <para>Never thrown by the running game. It is gated on
    /// <see cref="BattleExecutionContext.ThrowOnInertBattle"/>, which tests set and the game does
    /// not; see that property for why the same condition is fatal in one and survivable in the
    /// other. The battle is always shut down gracefully first, so anything that catches this sees a
    /// fully recorded <c>BattleHistory</c>.</para>
    /// </summary>
    public sealed class InertBattleException : Exception
    {
        public InertBattleException(string message) : base(message)
        {
        }
    }
}
