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
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class MeleeHitRateTests
{
    private const int Trials = 200_000;

    private static double HitRate(float attackSkill, float weaponAccuracy, bool didMove,
                                  float defenderSkill, float defenderEvasion,
                                  float defenderDefenseModifier = 0)
    {
        RNG.Reset(12345);
        int hits = 0;
        for (int i = 0; i < Trials; i++)
        {
            if (MeleeAttackAction.RollMeleeHit(attackSkill, weaponAccuracy, didMove,
                                               defenderSkill, defenderEvasion,
                                               defenderDefenseModifier))
            {
                hits++;
            }
        }
        return (double)hits / Trials;
    }

    [Fact]
    public void ParitySkill_NoEvasion_LandsAroundOneInFour()
    {
        // Equal skill, no weapon accuracy, no evasion: the C=3 defender advantage
        // should put this near 24%. Theoretical: P(N(0, 3*sqrt2) > 3) ~= 0.24.
        double rate = HitRate(attackSkill: 10, weaponAccuracy: 0, didMove: false,
                              defenderSkill: 10, defenderEvasion: 0);
        Assert.InRange(rate, 0.20, 0.28);
    }

    [Fact]
    public void StrongSkillEdge_LandsMoreOften()
    {
        // A 6-point skill advantage swamps the defender advantage; hits become common.
        double rate = HitRate(attackSkill: 16, weaponAccuracy: 0, didMove: false,
                              defenderSkill: 10, defenderEvasion: 0);
        Assert.InRange(rate, 0.50, 0.80);
    }

    [Fact]
    public void Evasion_StacksOnDefenderAdvantage_AndSuppressesHits()
    {
        // A slippery defender (evasion 3) on top of C=3 makes a parity attacker
        // rarely connect — the Genestealer/Ravener fantasy.
        double withoutEvasion = HitRate(10, 0, false, 10, 0);
        double withEvasion = HitRate(10, 0, false, 10, 3);
        // Effective barrier is C + evasion = 6, so P(N(0, 3*sqrt2) > 6) ~= 0.08.
        Assert.True(withEvasion < withoutEvasion);
        Assert.InRange(withEvasion, 0.05, 0.15);
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
