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

    [Theory]
    // Standing quarry: the pursuer keeps its whole move, so reach is move + allowance.
    [InlineData(6f, 0f, 7f, true)]
    [InlineData(6f, 0f, 7.01f, false)]
    // Genuinely faster pursuer: only the two-per-turn it actually gains counts.
    [InlineData(8f, 6f, 3f, true)]
    [InlineData(8f, 6f, 3.01f, false)]
    // The Xibarrus Theta fixed point (2026-08-04). Separation settles at exactly the pursuer's
    // move because that is how far it travels while the quarry travels the same, so measuring
    // against the raw move reported "contact is one move away" on every one of ~997 turns. Net of
    // the quarry's withdrawal the pursuer gains 0.001 a turn and can reach nothing.
    [InlineData(6.001f, 6.001f, 6f, false)]
    public void CanReachMeleeThisTurn_MeasuresNetClosingNotRawMove(
        float pursuerSpeed,
        float quarrySpeed,
        float separation,
        bool expected)
    {
        Assert.Equal(
            expected,
            BattleContactRules.CanReachMeleeThisTurn(separation, pursuerSpeed, quarrySpeed));
    }

    [Fact]
    public void SilentSternChaseAtMatchedSpeed_Disengages()
    {
        // Regression for the Xibarrus Theta ambush (2026-08-04): two Marine squads ran after one
        // Abominant at 6.001 vs 6.001 with the separation pinned at 6, landing nothing from turn 4
        // to the resolver's 1000-turn cap. The stalled_pursuit break should have ended it — the
        // pursuers had gone silent and could not close — but its "not standing on top of the
        // quarry" guard compared separation to the pursuer's raw move, which at matched speed is
        // exactly the separation. The guard is now net of the withdrawal, so the break fires.
        var sternChase = Input() with
        {
            MinimumCurrentSeparation = 6,
            FastestPursuerSpeed = 6.001f,
            SlowestWithdrawalSpeed = 6.001f,
            PursuersAttackedRecently = false
        };

        BattleContactRules.Result result = BattleContactRules.Evaluate(sternChase);

        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, result.Decision);
        Assert.Equal("stalled_pursuit", result.Reason);
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
    public void ReasonableShotKeepsSilentPursuitInContactWhileAimMatures()
    {
        var aimingPursuit = Input() with
        {
            MinimumCurrentSeparation = 9,
            FastestPursuerSpeed = 7,
            PursuersAttackedRecently = false,
            PursuersHaveReasonableShot = true
        };

        Assert.Equal(
            ContactBreakResult.RemainInContact,
            BattleContactRules.Evaluate(aimingPursuit).Decision);
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
        // Within a run-and-charge of the quarry: out of ammo is not out of contact. "Within a
        // charge" is net of the quarry's own withdrawal, so at the matched speeds this case holds
        // fixed it means the contact allowance and nothing more — the pursuer gains no ground, and
        // the eight yards this used to accept were eight yards it could never take back.
        Assert.Equal(ContactBreakResult.RemainInContact,
            BattleContactRules.Evaluate(stalled with { MinimumCurrentSeparation = 1 }).Decision);
        Assert.Equal(ContactBreakResult.OrganizedForceDisengages,
            BattleContactRules.Evaluate(stalled with { MinimumCurrentSeparation = 1.01f }).Decision);
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
                     "pursuer_speed=9 withdrawal_speed=7 pursuers_attacked=true " +
                     "pursuers_reasonable_shot=false rear_guard_active=false " +
                     "masked_progress=0 masked_required=7 decision=RemainInContact " +
                     "reason=pursuit_can_maintain_contact", trace);
    }
}
