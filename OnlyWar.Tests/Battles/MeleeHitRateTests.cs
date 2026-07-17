using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles.Actions;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Statistical guard rails for the contested melee roll (see
/// Design/EvasionBurrowAndAmbush.md). These assert that the per-swing hit rate
/// for representative matchups lands in a sane band — battles should neither
/// stalemate (nobody connects) nor end in a single exchange. The bands are wide
/// on purpose; they exist to catch a formula regression, not to pin an exact
/// probability.
/// </summary>
public class MeleeHitRateTests
{
    private const int Trials = 200_000;

    private static double HitRate(float attackSkill, float weaponAccuracy, bool didMove,
                                  float defenderSkill, float defenderEvasion,
                                  float defenderDefenseModifier = 0)
    {
        SeededRNG random = new(12345);
        int hits = 0;
        for (int i = 0; i < Trials; i++)
        {
            if (MeleeAttackAction.RollMeleeHit(attackSkill, weaponAccuracy, didMove,
                                               defenderSkill, defenderEvasion,
                                               defenderDefenseModifier,
                                               random))
            {
                hits++;
            }
        }
        return (double)hits / Trials;
    }

    [Fact]
    public void ParitySkill_NoEvasion_LandsAroundOneInTwo()
    {
        // Equal skill, no weapon accuracy, no evasion, zero defender advantage:
        // tabletop's "hit on 4s" — the contested roll should land near 50%.
        double rate = HitRate(attackSkill: 10, weaponAccuracy: 0, didMove: false,
                              defenderSkill: 10, defenderEvasion: 0);
        Assert.InRange(rate, 0.46, 0.54);
    }

    [Fact]
    public void StrongSkillEdge_LandsMoreOften_ButSaturatesGently()
    {
        // A 6-point skill advantage: with the sigma-6 dice each point is worth
        // ~5.6% near parity, so this lands near P(N(0, 6*sqrt2) > -6) ~= 0.76 —
        // clearly superior without approaching auto-hit.
        double rate = HitRate(attackSkill: 16, weaponAccuracy: 0, didMove: false,
                              defenderSkill: 10, defenderEvasion: 0);
        Assert.InRange(rate, 0.70, 0.82);
    }

    [Fact]
    public void Evasion_SuppressesHits_LikeAnInvulnerableSave()
    {
        // A slippery defender (evasion 3) versus a parity attacker: hits drop from
        // ~50% toward P(N(0, 6*sqrt2) > 3) ~= 0.36 — a meaningful invulnerable-save
        // style reduction, not untouchability.
        double withoutEvasion = HitRate(10, 0, false, 10, 0);
        double withEvasion = HitRate(10, 0, false, 10, 3);
        Assert.True(withEvasion < withoutEvasion);
        Assert.InRange(withEvasion, 0.31, 0.41);
    }

    [Fact]
    public void MovementPenalty_LowersHitRate()
    {
        double stationary = HitRate(10, 0, didMove: false, 10, 0);
        double moved = HitRate(10, 0, didMove: true, 10, 0);
        Assert.True(moved < stationary);
    }

    [Fact]
    public void DefenseModifier_LowersHitRate()
    {
        double unguarded = HitRate(10, 0, didMove: false, 10, 0, defenderDefenseModifier: 0);
        double guarded = HitRate(10, 0, didMove: false, 10, 0, defenderDefenseModifier: 1);

        Assert.True(guarded < unguarded);
    }
}
