using System;

using OnlyWar.Battles;
using OnlyWar.Domain.Equippables;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// The removal-rate table prices the fire soldiers really deliver (2026-10-23,
/// Design/Reference/ForecastCalibration.md section 1): the cycle the shot planner really fires
/// (RangedTargetSelector.PlannerCycle), and the aims lost when squadmates drop a shared target.
/// Before, it priced the best cycle on offer with every aim reaching its shot, and credited about
/// twice the fire PDF squads delivered.
/// </summary>
public class DeliveredFirePricingTests
{
    [Fact]
    public void ThePlannersCycle_FiresAtTheFirstBonusThatBeatsAimingOn_NotAtTheBestCycle()
    {
        // The full aim is the best cycle per turn (3.0 / 5 = 0.6), so a fresh soldier aims. One
        // turn in, the shot at bonus 0 (1.0) beats every further aim per turn it still takes (best
        // 1.6 / 2 = 0.8), so he fires there.
        float[] net = [1.0f, 1.6f, 2.2f, 3.0f];
        int[] turns = [2, 3, 4, 5];

        Assert.Equal(0, RangedTargetSelector.PlannerCycle(0.3f, net, turns));
        Assert.Equal(-1, RangedTargetSelector.PlannerCycle(0.7f, net, turns));
    }

    [Fact]
    public void ThePlannersCycle_AimsOnWhileEachFurtherTurnStillPays()
    {
        // Each aim turn more than doubles the shot, so at every bonus some further aim beats the
        // shot now per turn it takes (at bonus 2: 9 / 2 = 4.5 against 4): he aims to the end.
        float[] net = [1f, 2f, 4f, 9f];
        int[] turns = [2, 3, 4, 5];

        Assert.Equal(3, RangedTargetSelector.PlannerCycle(0.1f, net, turns));
    }

    [Fact]
    public void ASharedTarget_IsWorthLessToEachShooterThanSeparateOnes()
    {
        RangedWeaponTemplate rifle = Rifle();
        PairRemovalTerm Term(int shooter, int target) => new(
            shooter,
            target,
            rifle,
            referenceHitTotal: 8f,
            referenceShotsToFire: 1,
            referenceRange: 50f,
            referenceTargetSpeed: 0f,
            referenceTakeOut: 0.5f,
            referenceWoundProgress: 0f,
            targetBattleValue: 20f,
            maximumEffectiveRange: 200f,
            takeOutTerms: null,
            aimPlans:
            [
                new AimPlan(10f, 1, 2),
                new AimPlan(12f, 1, 3),
                new AimPlan(14f, 1, 4),
                new AimPlan(16f, 1, 5),
            ],
            standingShot: new AimPlan(6f, 1, 1));

        PairRemovalTerm first = Term(1, 101);
        SquadPairRemovalRate separate = new(1, 2, [first, Term(2, 102), Term(3, 103)]);
        SquadPairRemovalRate shared = new(1, 2, [first, Term(2, 101), Term(3, 101)]);

        Assert.True((first.SustainedCycleAt(50f).Turns) > 1, "fixture must make the cycle an aim");
        Assert.Equal(3 * first.SustainedRateAt(50f), separate.SustainedRateAtRange(50f), 4);
        Assert.True(
            shared.SustainedRateAtRange(50f) < separate.SustainedRateAtRange(50f),
            $"shared {shared.SustainedRateAtRange(50f):F3}, separate {separate.SustainedRateAtRange(50f):F3}");
    }

    private static RangedWeaponTemplate Rifle() => new(
        98_701,
        "Pricing rifle",
        EquipLocation.TwoHand,
        TestSkills.Ranged,
        accuracy: 3,
        armorMultiplier: 1,
        penetrationMultiplier: 1,
        requiredStrength: 0,
        baseDamage: 40,
        maxDistance: 200,
        rof: 1,
        ammo: 10,
        recoil: 0,
        bulk: 1,
        doesDamageDegradeWithRange: false,
        reloadTime: 1,
        ammunitionType: null);
}
