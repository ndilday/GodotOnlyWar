using OnlyWar.Helpers.Battles;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattleContactRulesTests
{
    private static BattleContactRules.Input Input() =>
        new(7, false, 2, false, false, 15, 10, 9, 7, false, 0, 7);

    [Fact]
    public void SlowerWithdrawal_CannotEscapeActiveFasterPursuit()
    {
        Assert.Equal(ContactBreakResult.RemainInContact,
            BattleContactRules.Evaluate(Input()).Decision);
    }

    [Fact]
    public void EqualSpeedWithdrawalBeyondAttackReach_OpensMobilityBreak()
    {
        var input = Input() with { FastestPursuerSpeed = 7 };

        Assert.Equal(ContactBreakResult.OrganizedForceDisengages,
            BattleContactRules.Evaluate(input).Decision);
    }

    [Fact]
    public void RearGuardMasksOnlyAfterFullRunAllowanceWhileActive()
    {
        var almost = Input() with { RearGuardActive = true, MaskedDepartureProgress = 6.99f };
        var enough = almost with { MaskedDepartureProgress = 7 };
        var inactive = enough with { RearGuardActive = false };

        Assert.Equal(ContactBreakResult.RemainInContact, BattleContactRules.Evaluate(almost).Decision);
        Assert.Equal(ContactBreakResult.SquadDisengages, BattleContactRules.Evaluate(enough).Decision);
        Assert.Equal(ContactBreakResult.RemainInContact, BattleContactRules.Evaluate(inactive).Decision);
    }

    [Fact]
    public void SpecialCapability_ImmediatelyDisengagesSquad()
    {
        var input = Input() with { HasImmediateDisengagementCapability = true };

        Assert.Equal(ContactBreakResult.SquadDisengages,
            BattleContactRules.Evaluate(input).Decision);
    }

    [Fact]
    public void PursuerStopping_DisengagesOrganizedForce()
    {
        var input = Input() with { AllPursuersBreakOff = true };

        Assert.Equal("pursuer_stops", BattleContactRules.Evaluate(input).Reason);
    }

    [Fact]
    public void TraceRenderer_UsesStableFields()
    {
        string trace = BattleContactRules.Evaluate(Input()).Trace.Render();

        Assert.Equal("CONTACT_EVAL turn=7 side=second active_pursuers=2 separation=15 attack_reach=10 " +
                     "pursuer_speed=9 withdrawal_speed=7 rear_guard_active=false masked_progress=0 " +
                     "masked_required=7 decision=RemainInContact reason=pursuit_can_maintain_contact", trace);
    }
}
