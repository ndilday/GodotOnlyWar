using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Selects the root action descriptor for one soldier under a projected squad posture.
    ///
    /// <para>This collaborator is deliberately a decision-only component. It reads the warmed
    /// planning state and returns <see cref="PlannedSoldierAction"/> data; it never emits an
    /// executable action, reserves movement, mutates a soldier, or draws from the battle RNG.
    /// The serial action builder later materializes the descriptor selected for the winning
    /// squad option.</para>
    /// </summary>
    internal sealed class SoldierActionPlanner
    {
        private const float WalkAimMultiplier = 0.5f;

        private readonly BattleGridManager _grid;
        private readonly IReadOnlyDictionary<int, BattleSoldier> _soldierMap;
        private readonly RangedTargetSelector _ranged;
        private readonly BlastThrowEvaluator _blast;

        internal SoldierActionPlanner(
            SquadPlanningServices services,
            RangedTargetSelector ranged,
            BlastThrowEvaluator blast)
        {
            ArgumentNullException.ThrowIfNull(services);
            _grid = services.Grid;
            _soldierMap = services.SoldierMap;
            _ranged = ranged ?? throw new ArgumentNullException(nameof(ranged));
            _blast = blast ?? throw new ArgumentNullException(nameof(blast));
        }

        internal PlannedSoldierAction PlanRootAction(
            BattleSoldier soldier,
            SquadMovementTier tier,
            float bulkMultiplier,
            ValueTuple<int, int>? movementDirection)
        {
            if (tier is SquadMovementTier.Run or SquadMovementTier.InMelee)
            {
                return PlanRunUtilityAction(soldier);
            }
            if (soldier.RangedWeapons.Count == 0)
            {
                return new PlannedSoldierAction(soldier.Soldier.Id, PlannedSoldierActionKind.None);
            }
            if (soldier.EquippedRangedWeapons.Count == 0)
            {
                RangedWeapon ready = soldier.RangedWeapons
                    .Where(weapon => (int)weapon.Template.Location <= soldier.FunctioningHands)
                    .OrderByDescending(weapon => weapon.Template.MaximumRange)
                    .ThenBy(weapon => weapon.Template.Id)
                    .FirstOrDefault();
                return ready == null
                    ? new PlannedSoldierAction(soldier.Soldier.Id, PlannedSoldierActionKind.None)
                    : new PlannedSoldierAction(
                        soldier.Soldier.Id,
                        PlannedSoldierActionKind.Ready,
                        WeaponTemplateId: ready.Template.Id,
                        ReadinessValue: GetBattleValue(soldier) * 0.025f);
            }

            RangedWeapon equipped = soldier.EquippedRangedWeapons[0];
            if (equipped.CanReload && (equipped.ReloadProgress > 0 || equipped.LoadedAmmo == 0))
            {
                return new PlannedSoldierAction(
                    soldier.Soldier.Id,
                    PlannedSoldierActionKind.Reload,
                    WeaponTemplateId: equipped.Template.Id,
                    ReadinessValue: GetBattleValue(soldier) * 0.025f);
            }

            if (tier == SquadMovementTier.Stationary
                && soldier.Aim is ValueTuple<int, RangedWeapon, int> stickyAim
                && _soldierMap.TryGetValue(stickyAim.Item1, out BattleSoldier stickyTarget)
                && _ranged.IsExistingAimStillViable(soldier))
            {
                float stickyRange = _grid.GetDistanceBetweenSoldiers(
                    soldier.Soldier.Id, stickyTarget.Soldier.Id);
                RangedTargetEvaluation stickyShot = _ranged.EvaluateRangedTarget(
                    soldier,
                    stickyTarget,
                    stickyAim.Item2,
                    stickyRange,
                    stickyAim.Item2.Template.Accuracy + stickyAim.Item3 + 1);
                bool shoot = stickyAim.Item3 >= 3
                    || stickyTarget.GetMoveSpeed() > stickyRange
                    || stickyShot.TakeOutProbabilityOnHit * stickyShot.HitProbability >= 0.33f;
                return shoot
                    ? PlanConventionalShot(soldier, stickyShot, 0, 1)
                    : new PlannedSoldierAction(
                        soldier.Soldier.Id,
                        PlannedSoldierActionKind.Aim,
                        stickyTarget.Soldier.Id,
                        stickyAim.Item2.Template.Id,
                        stickyRange,
                        ExpectedEnemyBattleValueRemoved:
                            stickyShot.ExpectedEnemyBattleValueRemoved,
                        ReadinessValue: EngagementPotential.ReadinessForPreparedShot(
                            soldier,
                            stickyShot));
            }

            float aimMultiplier = tier switch
            {
                SquadMovementTier.Stationary => 1f,
                SquadMovementTier.Walk => WalkAimMultiplier,
                _ => 0f
            };
            IReadOnlyList<BattleSoldier> candidates = _ranged.BuildRankedRangedCandidates(
                soldier,
                movementDirection);
            TemplateFiringLineEvaluation template = _ranged.SelectBestTemplateFiringLine(
                soldier,
                candidates,
                movementDirection);
            RangedTargetEvaluation targetEvaluation = _ranged.EvaluateStickyTarget(
                    soldier,
                    bulkMultiplier,
                    movementDirection)
                ?? _ranged.SelectBestRangedTarget(
                    soldier,
                    bulkMultiplier,
                    includeExistingAim: tier == SquadMovementTier.Stationary,
                    movementDirection: movementDirection);
            TemplateFiringLineEvaluation blast = _blast.SelectBestThrow(
                soldier,
                movementDirection,
                bulkMultiplier,
                candidates);
            float bestConventional = Math.Max(
                template?.Score ?? float.MinValue,
                targetEvaluation?.Score ?? float.MinValue);
            if (blast != null
                && blast.Score > bestConventional
                    + BlastThrowEvaluator.OverConventionalScoreMargin)
            {
                return new PlannedSoldierAction(
                    soldier.Soldier.Id,
                    PlannedSoldierActionKind.BlastAttack,
                    blast.Target.Soldier.Id,
                    blast.Weapon.Template.Id,
                    blast.Range,
                    BulkMultiplier: bulkMultiplier,
                    ExpectedEnemyBattleValueRemoved: blast.ExpectedEnemyBattleValueRemoved,
                    ExpectedFriendlyBattleValueLost: blast.ExpectedFriendlyBattleValueLost,
                    Diagnostic: _blast.FormatGrenadeSelection(
                        soldier,
                        blast,
                        targetEvaluation,
                        template,
                        bestConventional,
                        bulkMultiplier));
            }
            if (template != null
                && template.Score >= (targetEvaluation?.Score ?? float.MinValue))
            {
                return new PlannedSoldierAction(
                    soldier.Soldier.Id,
                    PlannedSoldierActionKind.AreaAttack,
                    template.Target.Soldier.Id,
                    template.Weapon.Template.Id,
                    template.Range,
                    BulkMultiplier: bulkMultiplier,
                    ExpectedEnemyBattleValueRemoved: template.ExpectedEnemyBattleValueRemoved,
                    ExpectedFriendlyBattleValueLost: template.ExpectedFriendlyBattleValueLost);
            }
            if (targetEvaluation == null)
            {
                RangedWeapon emptyBlast = soldier.EquippedRangedWeapons
                    .Concat(soldier.RangedWeapons)
                    .FirstOrDefault(weapon => weapon.Template.IsBlastWeapon
                        && weapon.LoadedAmmo == 0);
                return emptyBlast != null && emptyBlast.CanReload && emptyBlast.ReloadProgress == 0
                    ? new PlannedSoldierAction(
                        soldier.Soldier.Id,
                        PlannedSoldierActionKind.Reload,
                        WeaponTemplateId: emptyBlast.Template.Id,
                        ReadinessValue: GetBattleValue(soldier) * 0.02f)
                    : new PlannedSoldierAction(soldier.Soldier.Id, PlannedSoldierActionKind.None);
            }

            BattleSoldier target = targetEvaluation.Target;
            float range = _grid.GetDistanceBetweenSoldiers(
                soldier.Soldier.Id,
                target.Soldier.Id);
            if (soldier.Aim is ValueTuple<int, RangedWeapon, int> existingAim
                && existingAim.Item3 >= 3
                && existingAim.Item1 == target.Soldier.Id
                && existingAim.Item2.LoadedAmmo > 0
                && soldier.EquippedRangedWeapons.Contains(existingAim.Item2)
                && range <= existingAim.Item2.Template.MaximumRange)
            {
                float modifier = -(existingAim.Item2.Template.Bulk * bulkMultiplier)
                    + ((existingAim.Item2.Template.Accuracy + existingAim.Item3 + 1)
                        * aimMultiplier);
                return PlanConventionalShot(
                    soldier,
                    _ranged.EvaluateRangedTarget(
                        soldier,
                        target,
                        existingAim.Item2,
                        range,
                        modifier),
                    bulkMultiplier,
                    aimMultiplier);
            }

            RangedTargetEvaluation shootNow = _ranged.GetBestWeaponForSituation(
                soldier,
                target,
                range,
                bulkMultiplier,
                useAccuracy: false,
                aimMultiplier: aimMultiplier);
            // A moving candidate cannot aim. Excluding that illegal alternative, rather than
            // comparing against it and later doing nothing, is the key plan/execution invariant.
            RangedTargetEvaluation aimNow = aimMultiplier > 0
                ? _ranged.GetBestWeaponForSituation(
                    soldier,
                    target,
                    range,
                    bulkMultiplier,
                    useAccuracy: true,
                    aimMultiplier: aimMultiplier)
                : null;
            if (shootNow != null
                && (aimNow == null || shootNow.HitProbability * 2 > aimNow.HitProbability))
            {
                return PlanConventionalShot(
                    soldier,
                    shootNow,
                    bulkMultiplier,
                    aimMultiplier);
            }
            if (aimMultiplier > 0)
            {
                RangedWeapon aimWeapon = aimNow?.Weapon
                    ?? soldier.EquippedRangedWeapons
                        .Where(weapon => !weapon.Template.IsTemplateWeapon)
                        .OrderByDescending(weapon => weapon.Template.MaximumRange)
                        .ThenBy(weapon => weapon.Template.Id)
                        .FirstOrDefault();
                if (aimWeapon != null)
                {
                    return new PlannedSoldierAction(
                        soldier.Soldier.Id,
                        PlannedSoldierActionKind.Aim,
                        target.Soldier.Id,
                        aimWeapon.Template.Id,
                        range,
                        ExpectedEnemyBattleValueRemoved: aimNow?.ExpectedEnemyBattleValueRemoved ?? 0,
                        ReadinessValue: EngagementPotential.ReadinessForPreparedShot(
                            soldier,
                            aimNow));
                }
            }
            return new PlannedSoldierAction(soldier.Soldier.Id, PlannedSoldierActionKind.None);
        }

        private PlannedSoldierAction PlanRunUtilityAction(BattleSoldier soldier)
        {
            if (soldier.RangedWeapons.Count == 0)
            {
                return new PlannedSoldierAction(soldier.Soldier.Id, PlannedSoldierActionKind.None);
            }
            if (soldier.EquippedRangedWeapons.Count == 0)
            {
                RangedWeapon ready = soldier.RangedWeapons
                    .Where(weapon => (int)weapon.Template.Location <= soldier.FunctioningHands)
                    .OrderByDescending(weapon => weapon.Template.MaximumRange)
                    .ThenBy(weapon => weapon.Template.Id)
                    .FirstOrDefault();
                return ready == null
                    ? new PlannedSoldierAction(soldier.Soldier.Id, PlannedSoldierActionKind.None)
                    : new PlannedSoldierAction(
                        soldier.Soldier.Id,
                        PlannedSoldierActionKind.Ready,
                        WeaponTemplateId: ready.Template.Id,
                        ReadinessValue: GetBattleValue(soldier) * 0.025f);
            }
            RangedWeapon equipped = soldier.EquippedRangedWeapons[0];
            RangedWeapon weapon = equipped.CanReload
                && (equipped.ReloadProgress > 0 || equipped.LoadedAmmo == 0)
                    ? equipped
                    : soldier.RangedWeapons.FirstOrDefault(candidate =>
                        candidate.Template.IsBlastWeapon
                        && candidate.CanReload
                        && candidate.LoadedAmmo == 0);
            return weapon == null
                ? new PlannedSoldierAction(soldier.Soldier.Id, PlannedSoldierActionKind.None)
                : new PlannedSoldierAction(
                    soldier.Soldier.Id,
                    PlannedSoldierActionKind.Reload,
                    WeaponTemplateId: weapon.Template.Id,
                    ReadinessValue: GetBattleValue(soldier) * 0.025f);
        }

        private static PlannedSoldierAction PlanConventionalShot(
            BattleSoldier soldier,
            RangedTargetEvaluation shot,
            float bulkMultiplier,
            float aimMultiplier) => new(
            soldier.Soldier.Id,
            PlannedSoldierActionKind.Shoot,
            shot.Target.Soldier.Id,
            shot.Weapon.Template.Id,
            shot.Range,
            shot.ShotsToFire,
            bulkMultiplier,
            aimMultiplier,
            shot.ExpectedEnemyBattleValueRemoved,
            shot.ExpectedFriendlyBattleValueLost);

        private static float GetBattleValue(BattleSoldier soldier) =>
            SquadPlanningServices.BattleValueOf(soldier);
    }
}
