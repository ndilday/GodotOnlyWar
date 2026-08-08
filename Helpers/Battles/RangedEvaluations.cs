using System.Collections.Generic;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// A scored single-target ranged action: which weapon, at what range, for how many shots, and
    /// what it is expected to remove. Produced by the planner's target selection and consumed by
    /// the Phase 4 removal-rate table.
    /// </summary>
    internal sealed class RangedTargetEvaluation
    {
        public BattleSoldier Target { get; }
        public RangedWeapon Weapon { get; }
        public float Range { get; }
        public int ShotsToFire { get; }
        public float HitProbability { get; }
        public float TakeOutProbabilityOnHit { get; }
        public float ExpectedEnemyBattleValueRemoved { get; }
        public float ExpectedFriendlyBattleValueLost { get; }
        // The pre-roll to-hit total behind HitProbability (HitProbability ==
        // Phi((PreRollHitTotal - 10.5)/3)) and the target speed the range modifier was taken
        // at. Recorded rather than re-derived so the Phase 4 removal-rate table can rescale
        // this shot to another range in closed form -- inverting the CDF would be lossy, and
        // recomputing the total would duplicate RangedHitEstimateContext's assembly order.
        // See Design/Reference/EngagementScoringOverhaul.md.
        public float PreRollHitTotal { get; }
        public float TargetSpeed { get; }
        // Phase 5: E[woundProgress; no takeout] for this shot, captured alongside the take-out
        // probability so the Phase 4 removal-rate table can carry the graded term without a
        // second hit-location walk. See RemovalMath.CalculateRemovalFractionOnHit.
        public float WoundProgressOnHit { get; }
        public float Score => ExpectedEnemyBattleValueRemoved - ExpectedFriendlyBattleValueLost;

        public RangedTargetEvaluation(
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            int shotsToFire,
            float hitProbability,
            float takeOutProbabilityOnHit,
            float expectedEnemyBattleValueRemoved,
            float expectedFriendlyBattleValueLost,
            float preRollHitTotal = 0f,
            float targetSpeed = 0f,
            float woundProgressOnHit = 0f)
        {
            WoundProgressOnHit = woundProgressOnHit;
            Target = target;
            Weapon = weapon;
            Range = range;
            ShotsToFire = shotsToFire;
            HitProbability = hitProbability;
            TakeOutProbabilityOnHit = takeOutProbabilityOnHit;
            ExpectedEnemyBattleValueRemoved = expectedEnemyBattleValueRemoved;
            ExpectedFriendlyBattleValueLost = expectedFriendlyBattleValueLost;
            PreRollHitTotal = preRollHitTotal;
            TargetSpeed = targetSpeed;
        }
    }

    /// <summary>
    /// A scored multi-body ranged action -- a flamer cone or a grenade throw -- named for the aim
    /// point that anchors it. Both halves of the trade are carried separately so the caller can see
    /// what a throw cost its own side, not just the net.
    /// </summary>
    internal sealed class TemplateFiringLineEvaluation
    {
        public BattleSoldier Target { get; }
        public RangedWeapon Weapon { get; }
        public float Range { get; }
        public IReadOnlyList<int> VictimIds { get; }
        public float ExpectedEnemyBattleValueRemoved { get; }
        public float ExpectedFriendlyBattleValueLost { get; }
        public float Score => ExpectedEnemyBattleValueRemoved - ExpectedFriendlyBattleValueLost;

        public TemplateFiringLineEvaluation(
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            IReadOnlyList<int> victimIds,
            float expectedEnemyBattleValueRemoved,
            float expectedFriendlyBattleValueLost)
        {
            Target = target;
            Weapon = weapon;
            Range = range;
            VictimIds = victimIds;
            ExpectedEnemyBattleValueRemoved = expectedEnemyBattleValueRemoved;
            ExpectedFriendlyBattleValueLost = expectedFriendlyBattleValueLost;
        }
    }
}
