using System.Linq;

namespace OnlyWar.Models.Soldiers
{
    /// <summary>
    /// What a battle did to a soldier, resolved once the fighting stops
    /// (Design/Reference/CasualtyRealism.md §2.3). Ordered from least to most severe so the
    /// worst outcome wins a comparison.
    /// </summary>
    public enum CasualtyState
    {
        /// <summary>No wounds at all.</summary>
        Unharmed = 0,
        /// <summary>
        /// Wounded but still a participant: he can bring a weapon to bear and he can walk.
        /// Normal recovery, no special handling. Phase 3 replaces the binary motive test with a
        /// graded speed multiplier, at which point "impaired" acquires degrees.
        /// </summary>
        Impaired = 1,
        /// <summary>
        /// Out of the fight but ALIVE: motive capability gone, a weapon hand ruined, or a vital
        /// location crippled short of severed. Power-armor biostasis means he cannot die of these
        /// wounds while he waits, so this is a casualty and never a kill -- provided his side is
        /// standing on the ground he fell on when the shooting stops.
        /// </summary>
        Incapacitated = 2,
        /// <summary>
        /// A vital location was severed, or he went down on ground his side did not hold and was
        /// never recovered. Fallen-brother / gene-seed / death-record path.
        /// </summary>
        Killed = 3
    }

}
