using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using System.Collections.Generic;

namespace OnlyWar.Domain
{
    /// <summary>
    /// A transport action's complete passenger selection. A character batch is still a normal
    /// movement party; selecting every member of an administrative formation is only a UI
    /// convenience and never turns that formation into a campaign movement squad.
    /// </summary>
    public sealed record MovementParty(
        IReadOnlyList<Squad> Squads,
        IReadOnlyList<PlayerSoldier> Characters)
    {
        public static MovementParty Empty { get; } = new([], []);
    }
}
