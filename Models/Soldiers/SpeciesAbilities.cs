using System;

namespace OnlyWar.Models.Soldiers
{
    /// <summary>
    /// Engine-interpreted special capabilities a species may possess. Stored as a
    /// single [Flags] integer column on the Species table (see the migrate-evasion
    /// pass in RulesDbTool). Sized to 32 bits for now; doubling to a long is an
    /// acceptable one-time migration if we ever exhaust these.
    /// </summary>
    [Flags]
    public enum SpeciesAbilities
    {
        None = 0,
        /// <summary>
        /// The species can tunnel underground. Mechanically this lets a unit (a)
        /// erupt into melee — placed adjacent to an enemy at battle start instead of
        /// at engagement range — and (b) disengage from battle immediately, bypassing
        /// the normal withdrawal sequence (PRD §6.4).
        /// </summary>
        Burrow = 1 << 0,
        /// <summary>
        /// The species projects a synapse aura: a living, same-faction squad carrying
        /// this ability grants morale-check immunity to friendly squads within its
        /// <see cref="Species.SynapseRadius"/> (Design/Active/MoraleAndRout.md §4).
        /// Coverage is derived per turn from post-round state, never stored.
        /// </summary>
        Synapse = 1 << 1,
    }
}
