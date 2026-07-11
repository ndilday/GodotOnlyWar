using System;

namespace OnlyWar.Helpers.Missions
{
    // Centralized tunables and formula for "learn by doing" field experience (PRD §4.12).
    // Every mission skill check (IndividualMissionTest / LeaderMissionTest / SquadMissionTest,
    // see MissionCheck.cs) resolves to a signed margin from
    // GaussianCalculator.DetermineMarginOfSuccessZvalue: positive means success, magnitude is
    // how comfortably it succeeded or failed. This calculator turns that margin into skill
    // points awarded, in SkillUsed, to every able participating soldier.
    //
    // Design intent (mirrors PRD §4.12):
    //   - A trivial success (large positive margin) should teach almost nothing — the soldier
    //     was never challenged.
    //   - A near-miss (margin close to zero, whether a narrow win or a narrow loss) should
    //     teach the most: this is the "edge of ability" zone where real learning happens.
    //   - A hopeless failure (large negative margin) should also teach little — a badly
    //     mismatched task doesn't reward the failing soldier with much insight either, and this
    //     specifically kills any incentive to field deliberately under-skilled squads to farm
    //     XP (fielding weak soldiers just buries the margin deep in the "hopeless" zone, where
    //     the award decays same as a trivial win).
    //
    // Formula: a Gaussian "bump" in margin, centered slightly below zero (a narrow failure
    // teaches at least as much as a narrow success, never less), with a single spread constant
    // controlling how quickly the award falls off in both directions:
    //
    //   points = BasePointsPerCheck * exp(-(margin - BumpCenterMargin)^2 / (2 * BumpSpread^2))
    //
    // This is smooth, bounded in (0, BasePointsPerCheck], symmetric-ish around the bump center,
    // and decays toward zero for both trivial wins and deep losses. Applied per able
    // participating PlayerSoldier per check; there is deliberately no per-mission cap — the
    // geometric skill-point cost curve (Skill.SkillBonus = log2(pointsInvested) - difficulty)
    // is the intended governor, so green/mid soldiers gain fast and veterans taper off on their
    // own without any code-side clamp.
    public static class MissionExperienceCalculator
    {
        // Peak per-check, per-soldier award, hit when margin == BumpCenterMargin. Calibrated to
        // clear an equivalent week of garrison training in the exercised skill. Weekly
        // work-experience training (TurnController.WeeklyTrainingPoints = 0.2) is a *shared*
        // budget split across every entry in a soldier's training profile (commonly 3-5
        // skill/attribute entries via SoldierTrainingCalculator.ApplyTrainingProfile), so any one
        // skill typically nets on the order of 0.04-0.07 points/week from drilling. A single
        // well-rolled field check landing near the bump center awards more than that on its own,
        // and a mission runs several checks, so an eventful mission clearly outpaces a week of
        // garrison drills for the tested skill — while a mission's checks are spread across
        // whichever skills actually got exercised, same as training spreads across a profile.
        public const float BasePointsPerCheck = 0.08f;

        // The margin at which award is maximal. Slightly negative so a narrow failure teaches
        // at least as much as an equally-narrow success — we're not rewarding failure *as such*,
        // just declining to penalize the honest near-miss relative to the honest near-win.
        public const float BumpCenterMargin = -0.25f;

        // Standard-deviation-like spread of the bump, in margin units (margin is itself in
        // roughly-z-score units, see GaussianCalculator). Small enough that a comfortably-won
        // check (margin >= ~2) or a badly-lost one (margin <= ~-2.5) has fallen off to a small
        // fraction of the peak, but wide enough that ordinary contested checks (|margin| <~ 1)
        // stay close to peak value.
        public const float BumpSpread = 1.25f;

        // Points awarded for a single check with the given margin. Always positive (a
        // Gaussian bump never reaches zero), so every check teaches at least a little.
        public static float CalculatePointsForMargin(float margin)
        {
            float delta = margin - BumpCenterMargin;
            float exponent = -(delta * delta) / (2f * BumpSpread * BumpSpread);
            return BasePointsPerCheck * (float)Math.Exp(exponent);
        }
    }
}
