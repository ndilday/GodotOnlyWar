using OnlyWar.Battles;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class PursuitPairActivityTests
{
    private static PursuitPairActivity Activity(
        float pursuerSpeed = 7,
        float quarrySpeed = 7,
        bool pairAttackedRecently = false,
        bool fireCycleProgressedThisTurn = false,
        bool fireCommitmentRemainsViable = false) =>
        new(
            PursuerSquadId: 11,
            QuarrySquadId: 22,
            CurrentSeparation: 15,
            PursuerDeclaredSpeed: pursuerSpeed,
            QuarryWithdrawalSpeed: quarrySpeed,
            PairAttackedRecently: pairAttackedRecently,
            FireCycleProgressedThisTurn: fireCycleProgressedThisTurn,
            FireCommitmentRemainsViable: fireCommitmentRemainsViable);

    [Fact]
    public void PositivePairwiseClosingSpeed_QualifiesAsActivePursuit()
    {
        PursuitPairActivity activity = Activity(pursuerSpeed: 8, quarrySpeed: 7);

        Assert.True(activity.HasMeaningfulPositiveClosingSpeed);
        Assert.True(activity.HasActivePursuitEvidence);
    }

    [Theory]
    [InlineData(7f, 7f)]
    [InlineData(6f, 7f)]
    [InlineData(7.1f, 7f)]
    public void EqualOrInsignificantlyHigherSpeed_DoesNotQualifyByItself(
        float pursuerSpeed,
        float quarrySpeed)
    {
        PursuitPairActivity activity = Activity(pursuerSpeed, quarrySpeed);

        Assert.False(activity.HasMeaningfulPositiveClosingSpeed);
        Assert.False(activity.HasActivePursuitEvidence);
    }

    [Fact]
    public void RecentPairAttack_QualifiesAsActivePursuit()
    {
        PursuitPairActivity activity = Activity(pairAttackedRecently: true);

        Assert.False(activity.HasMeaningfulPositiveClosingSpeed);
        Assert.True(activity.HasActivePursuitEvidence);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void FireCycleProgress_QualifiesOnlyWhileCommitmentRemainsViable(
        bool fireCycleProgressedThisTurn,
        bool fireCommitmentRemainsViable,
        bool expectedEvidence)
    {
        PursuitPairActivity activity = Activity(
            fireCycleProgressedThisTurn: fireCycleProgressedThisTurn,
            fireCommitmentRemainsViable: fireCommitmentRemainsViable);

        Assert.Equal(expectedEvidence, activity.HasQualifyingFireCycleProgress);
        Assert.Equal(expectedEvidence, activity.HasActivePursuitEvidence);
    }

    [Fact]
    public void NoPairEvidence_DoesNotQualifyAsActivePursuit()
    {
        PursuitPairActivity activity = Activity();

        Assert.False(activity.HasMeaningfulPositiveClosingSpeed);
        Assert.False(activity.HasQualifyingFireCycleProgress);
        Assert.False(activity.HasActivePursuitEvidence);
    }
}
