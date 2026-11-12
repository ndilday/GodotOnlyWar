using System.Linq;

using OnlyWar.Battles;

using Xunit;

namespace OnlyWar.Tests.Battles
{
    /// <summary>
    /// The contact rate's frontage rule (Design/Active/ContactRateCalibration.md):
    /// <see cref="MeleeStrikeEstimator.ContactStrikes.RemovalRate"/>.
    /// </summary>
    public class ContactFrontageTests
    {
        private static MeleeStrikeEstimator.ContactStrikes Strikes(int attackers, float takeOut, int defenders) =>
            new(Enumerable.Repeat(takeOut, attackers).ToArray(), defenders, defenders * 5f);

        [Fact]
        public void AttackersBeyondTheFrontage_AddNothing()
        {
            float atFrontage = Strikes(30, 0.9f, 20).RemovalRate();
            float overwhelming = Strikes(60, 0.9f, 20).RemovalRate();

            Assert.Equal(atFrontage, overwhelming, 3);
        }

        [Fact]
        public void RandomContact_LeavesDefendersUnmet()
        {
            // Twelve certain killers against forty defenders reach about 10, not 12.
            float removed = Strikes(12, 1f, 40).RemovalRate() / 5f;

            Assert.InRange(removed, 9f, 11f);
        }

        [Fact]
        public void RemovalNeverExceedsTheDefendersValue()
        {
            MeleeStrikeEstimator.ContactStrikes strikes = Strikes(100, 1f, 10);

            Assert.True(strikes.RemovalRate() <= strikes.DefenderValue);
        }

        [Fact]
        public void SharedFrontage_SumsToNoMoreThanOneSquadAlone()
        {
            // Three broods of 20 on a squad of 20: sharing its frontage by numbers, the three
            // together remove no more than one brood of 60 would.
            MeleeStrikeEstimator.ContactStrikes brood = Strikes(20, 0.9f, 20);
            float each = brood.EngagedTakeOuts(frontageShare: 1f / 3f);
            float shared = 3 * brood.RemovalRate(frontageShare: 1f / 3f, otherTakeOuts: 2 * each);
            float alone = Strikes(60, 0.9f, 20).RemovalRate();

            Assert.Equal(alone, shared, 2);
            Assert.True(shared < 3 * brood.RemovalRate());
        }

        [Fact]
        public void DefenderShare_ScalesThePoolFought()
        {
            MeleeStrikeEstimator.ContactStrikes strikes = Strikes(10, 0.8f, 20);

            Assert.True(strikes.RemovalRate(defenderShare: 0.5f) <= 0.5f * strikes.DefenderValue);
            Assert.True(strikes.RemovalRate(defenderShare: 0.5f) > 0f);
        }

        [Fact]
        public void NoAttackers_RemoveNothing()
        {
            Assert.Equal(0f, MeleeStrikeEstimator.ContactStrikes.None.RemovalRate());
        }
    }
}
