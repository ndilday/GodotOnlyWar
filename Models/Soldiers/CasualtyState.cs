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

    /// <summary>
    /// Classifies a soldier's post-battle condition from his body alone, so the battle aftermath,
    /// the debrief, and the tests all read the same rule. Deliberately free of battle types: the
    /// only external fact it needs is whether the body could be recovered.
    /// </summary>
    public static class CasualtyStateEvaluator
    {
        /// <summary>
        /// A vital location taken off entirely. This -- not a crippled vital -- is what kills a
        /// player soldier, and it has been the live rule in
        /// <c>PlayerChapterBattleAftermathPolicy</c> since before this state was named.
        /// </summary>
        public static bool HasSeveredVitalLocation(ISoldier soldier) =>
            soldier?.Body?.HitLocations
                .Any(location => location.Template.IsVital && location.IsSevered)
            ?? false;

        /// <summary>
        /// Down but not dead: no severed vital, yet no longer able to fight or to move. This is
        /// exactly the predicate the wound resolver uses to pull a soldier out of the battle
        /// (<see cref="ISoldier.IsCombatEffective"/>), minus the men it pulled out by killing.
        /// </summary>
        public static bool IsIncapacitated(ISoldier soldier) =>
            soldier != null
            && !HasSeveredVitalLocation(soldier)
            && !soldier.IsCombatEffective;

        public static bool IsWounded(ISoldier soldier) =>
            soldier?.Body?.HitLocations.Any(location => location.Wounds.WoundTotal > 0) ?? false;

        /// <param name="bodyRecovered">
        /// True when the soldier's own side held the field, so the wounded were carried off it.
        /// A brother left where he fell is presumed dead however survivable his wounds were --
        /// biostasis keeps him alive for his own side to find, not for the enemy's.
        /// </param>
        public static CasualtyState Classify(ISoldier soldier, bool bodyRecovered)
        {
            if (soldier == null) return CasualtyState.Unharmed;
            if (HasSeveredVitalLocation(soldier)) return CasualtyState.Killed;
            if (!soldier.IsCombatEffective)
            {
                return bodyRecovered ? CasualtyState.Incapacitated : CasualtyState.Killed;
            }
            return IsWounded(soldier) ? CasualtyState.Impaired : CasualtyState.Unharmed;
        }
    }
}
