using System;

namespace OnlyWar.Helpers.Battles
{
    // "Learn by doing" for battle skills, mirroring MissionExperienceCalculator (PRD §4.12) but for
    // per-attack combat rolls. Every shoot/melee roll produces a realized margin — how comfortably
    // the attack hit or missed — and this turns that margin into skill points awarded to the weapon's
    // related skill. Same design intent as missions:
    //   - A trivial hit (large positive margin: an easy shot at a slow, unarmored, close target)
    //     teaches almost nothing.
    //   - A near-miss / hard-won hit (margin close to zero) teaches the most — the edge of ability.
    //   - A hopeless miss (large negative margin) teaches little, and mowing down weak foes never
    //     becomes an XP farm.
    //
    // The caller normalizes each weapon's raw roll gap into z-units before calling in, so ranged
    // (roll sigma 3) and melee (opposed-roll sigma ~8.49) feed the same curve:
    //   ranged: marginZ = (skill + mods - roll) / 3
    //   melee:  marginZ = (attackTotal - defendTotal) / OpposedRollSigma
    //
    // Attributes are NOT driven by this — physical conditioning (Str/Dex from reps, Con from wounds
    // taken) stays use-count-based in the battle aftermath, because it doesn't depend on how hard a
    // given roll was. Only skill learning is roll/margin-based.
    //
    // Accrued per roll onto the BattleSoldier and applied once, to PlayerSoldiers only, in
    // PlayerChapterBattleAftermathPolicy.ApplySoldierExperienceForBattle — so mid-battle skill values
    // don't drift and battle replays can't double-apply.
    public static class BattleExperienceCalculator
    {
        // Peak per-roll, per-soldier award, hit when marginZ == BumpCenterMargin. Much smaller than
        // the mission per-check peak (0.053): a battle resolves far more rolls than a week has mission
        // checks, so the per-roll grain must be finer. STARTING VALUE — tune against the "Battle XP ["
        // debug log (SoldierProgressLog) the same way the mission constant was calibrated.
        public const float BasePointsPerRoll = 0.02f;

        // Same shape as the mission bump: maximal just below the hit threshold so a narrow miss
        // teaches at least as much as an equally narrow hit, symmetric falloff to either side.
        public const float BumpCenterMargin = -0.25f;
        public const float BumpSpread = 1.25f;

        // A melee defender also learns from every swing aimed at them (their skill/parry/evasion is
        // part of the contested roll), scaled by the *defender's* margin (-attacker margin) so a
        // hard-turned near-miss teaches most. Defending is secondary to attacking, so the award is
        // discounted by this factor relative to the attacker's.
        public const float MeleeDefenderShare = 0.5f;

        public static float CalculatePointsForMargin(float marginZ)
        {
            float delta = marginZ - BumpCenterMargin;
            float exponent = -(delta * delta) / (2f * BumpSpread * BumpSpread);
            return BasePointsPerRoll * (float)Math.Exp(exponent);
        }
    }
}
