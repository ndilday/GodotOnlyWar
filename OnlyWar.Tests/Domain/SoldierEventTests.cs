using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using Xunit;

namespace OnlyWar.Tests.Domain;

// The structured event log replaced free-text soldier history (PRD 0.8 step 1).
// Render() must reproduce the legacy display lines so the existing history surface is
// unchanged: every event is date-stamped except the death note, which never was.
public class SoldierEventTests
{
    private readonly Date _date = new(41, 999, 12);

    [Theory]
    [InlineData(SoldierEventType.Founding, "voted by the chapter to become the first Chapter Master")]
    [InlineData(SoldierEventType.AcceptedToTraining, "accepted into training")]
    [InlineData(SoldierEventType.Promotion, "Promoted to Codicier and assigned to Librarius")]
    [InlineData(SoldierEventType.Transfer, "transferred to Tactical Squad")]
    [InlineData(SoldierEventType.RatingFlag, "Flagged for potential training as Apothecary")]
    [InlineData(SoldierEventType.AwardReceived, "Awarded Bronze Sword of the Emperor")]
    [InlineData(SoldierEventType.BattleParticipation, "Skirmish in the Hive, Terra. Felled 3 Tyranids.")]
    public void Render_StampsNonDeathEventsWithTheDate(SoldierEventType type, string detail)
    {
        SoldierEvent soldierEvent = new(_date, type, detail);

        Assert.Equal($"{_date}: {detail}", soldierEvent.Render());
    }

    [Fact]
    public void Render_DeathEvent_OmitsDateStampToMatchLegacyLine()
    {
        SoldierEvent death = new(_date, SoldierEventType.Death,
            "Killed in battle with the Tyranids by a Scything Talon");

        Assert.Equal("Killed in battle with the Tyranids by a Scything Talon", death.Render());
    }

    [Fact]
    public void Constructor_DefaultsOptionalStructuredFields()
    {
        SoldierEvent soldierEvent = new(_date, SoldierEventType.Promotion, "promoted");

        Assert.Null(soldierEvent.FactionId);
        Assert.Null(soldierEvent.WeaponTemplateId);
        Assert.Null(soldierEvent.Magnitude);
        Assert.Null(soldierEvent.LocationName);
        Assert.Empty(soldierEvent.RelatedSoldierIds);
    }
}
