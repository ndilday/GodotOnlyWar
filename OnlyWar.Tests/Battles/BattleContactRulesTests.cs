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
    public void TrivialPursuerSpeedEdge_StillOpensMobilityBreak()
    {
        // Within the tolerance the pursuer would need hundreds of turns to make up a single hex.
        var withinTolerance = Input() with { FastestPursuerSpeed = 7.1f };
        var beyondTolerance = Input() with { FastestPursuerSpeed = 7.2f };

        Assert.Equal(ContactBreakResult.OrganizedForceDisengages,
            BattleContactRules.Evaluate(withinTolerance).Decision);
        Assert.Equal(ContactBreakResult.RemainInContact,
            BattleContactRules.Evaluate(beyondTolerance).Decision);
    }

    [Fact]
    public void SilentPursuitThatCannotClose_DisengagesInsideMaximumWeaponRange()
    {
        // Separation sits inside the pursuer's nominal attack reach, so the mobility break never
        // fires — but the pursuer has landed nothing and cannot close, which is a chase in name
        // only.
        var stalled = Input() with
        {
            MinimumCurrentSeparation = 9,
            FastestPursuerSpeed = 7,
            PursuersAttackedRecently = false
        };

        BattleContactRules.Result result = BattleContactRules.Evaluate(stalled);

        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, result.Decision);
        Assert.Equal("stalled_pursuit", result.Reason);
    }

    [Fact]
    public void StalledPursuitBreak_RequiresSilence_NoSpeedEdge_AndMeleeSeparation()
    {
        var stalled = Input() with
        {
            MinimumCurrentSeparation = 9,
            FastestPursuerSpeed = 7,
            PursuersAttackedRecently = false
        };

        // Still shooting: the running firefight is a real engagement.
        Assert.Equal(ContactBreakResult.RemainInContact,
            BattleContactRules.Evaluate(stalled with { PursuersAttackedRecently = true }).Decision);
        // Faster pursuer: it will close and the silence is temporary.
        Assert.Equal(ContactBreakResult.RemainInContact,
            BattleContactRules.Evaluate(stalled with { FastestPursuerSpeed = 9 }).Decision);
        // Within a run-and-charge of the quarry: out of ammo is not out of contact.
        Assert.Equal(ContactBreakResult.RemainInContact,
            BattleContactRules.Evaluate(stalled with { MinimumCurrentSeparation = 8 }).Decision);
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
                     "pursuer_speed=9 withdrawal_speed=7 pursuers_attacked=true rear_guard_active=false " +
                     "masked_progress=0 masked_required=7 decision=RemainInContact " +
                     "reason=pursuit_can_maintain_contact", trace);
    }
}
