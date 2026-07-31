using OnlyWar.Models.Orders;
using System;

namespace OnlyWar.Helpers.Missions
{
    // Aggression trades EXPOSURE for EFFECT (OnlyWar_TDD.md §6.4).
    //
    // Before this, Aggression did exactly one thing: it set the casualty fraction at which a force
    // withdrew from an engagement (BattleForceEvaluator.GetEligibilityThreshold). That remains
    // untouched. This adds the second meaning: nearly every mission has an axis along which caution
    // keeps the force safe and an axis along which boldness gets the job done, and aggression is the
    // slider between them. A cautious recon sweep stays hidden and learns little; a bold one presses
    // close, learns more, and gets seen.
    //
    // Both modifiers are expressed as DIFFICULTY deltas, because that is the only currency every
    // mission check shares (IndividualMissionTest / LeaderMissionTest / SquadMissionTest all convert
    // via (skill - difficulty) / 5). Negative makes a check easier, positive makes it harder.
    //
    // One shared magnitude is used across every mission type rather than per-type tuning. That was a
    // deliberate call: a single scale keeps the setting legible to the player, who otherwise has to
    // learn what "Cautious" means separately for each order type. Split it only if play demands it.
    public static class MissionAggressionModifiers
    {
        // Difficulty shift per step away from Normal. The mission-check conversion is
        // (skill - difficulty) / 5, so one difficulty point is 0.2 sigma; at 0.5 the full
        // Avoid..Aggressive span is 2.0 difficulty, or 0.4 sigma. That is deliberately comparable to
        // the patrol term's usual contribution in MissionStealthDifficulty rather than larger than
        // it: aggression should be a real lever on a stealth check without becoming the whole check,
        // which is the failure mode the search-effort model was built to escape.
        public const float DifficultyStepPerLevel = 0.5f;

        // How many steps from Normal, signed toward boldness.
        private static int StepsFromNormal(Aggression aggression) => aggression switch
        {
            Aggression.Avoid => -2,
            Aggression.Cautious => -1,
            Aggression.Normal => 0,
            Aggression.Attritional => 1,
            Aggression.Aggressive => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(aggression), aggression, null)
        };

        /// <summary>
        /// Difficulty delta for a check the force wants to pass by NOT being noticed - a stealth
        /// approach, staying concealed, avoiding a fight. Caution helps: the modifier is negative
        /// below Normal and positive above it.
        /// </summary>
        public static float ExposureDifficulty(Aggression aggression) =>
            StepsFromNormal(aggression) * DifficultyStepPerLevel;

        /// <summary>
        /// Difficulty delta for a check that accomplishes the objective - intelligence gathered,
        /// the shot taken, charges placed. Boldness helps, so this is the exact inverse of
        /// <see cref="ExposureDifficulty"/>: a force that will not expose itself cannot press
        /// close enough to learn much.
        /// </summary>
        public static float EffectDifficulty(Aggression aggression) =>
            -ExposureDifficulty(aggression);
    }
}
