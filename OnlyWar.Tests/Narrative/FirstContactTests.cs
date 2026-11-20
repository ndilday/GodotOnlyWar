using System.Collections.Generic;
using System.IO;
using OnlyWar.Campaign.Events;
using OnlyWar.Domain;
using OnlyWar.Domain.Events;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Narrative;

// First contact is the Chapter's own: the Chronicle says "the Chapter first learned the measure of"
// a faction and enters it "among the threats to the sector". The Imperium was entered as a threat,
// and the Imperium's own week-0 confirmations during sector generation made Chronicle entries
// titled "Campaign Service Record" that recalled the intel reading behind them (user reports,
// 2026-11-19).
public class FirstContactTests
{
    private readonly GameRulesData _data;

    public FirstContactTests()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        _data = OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(RulesDatabaseFixture.DatabasePath);
    }

    [Fact]
    public void TheChapterMakesFirstContactWithAHostileFaction()
    {
        Assert.True(CampaignEventRecorder.IsFirstContact(_data.PlayerFaction, _data.SectorFactions.Invader, null));
        Assert.True(CampaignEventRecorder.IsFirstContact(_data.PlayerFaction, _data.SectorFactions.Infiltrator, null));
    }

    [Fact]
    public void TheImperiumIsNeverAFirstContact()
    {
        // No planet means no relationship ledger, where every pair otherwise resolves as hostile.
        Assert.False(CampaignEventRecorder.IsFirstContact(_data.PlayerFaction, _data.DefaultFaction, null));
        Assert.False(CampaignEventRecorder.IsFirstContact(_data.DefaultFaction, _data.PlayerFaction, null));
    }

    [Fact]
    public void AnotherFactionsObservationIsNotTheChaptersFirstContact()
    {
        Assert.False(CampaignEventRecorder.IsFirstContact(_data.DefaultFaction, _data.SectorFactions.Invader, null));
        Assert.False(CampaignEventRecorder.IsFirstContact(_data.DefaultFaction, _data.SectorFactions.Infiltrator, null));
    }

    [Fact]
    public void FirstContactEntryHasItsOwnTitleAndNoIntelCallback()
    {
        CampaignEventEntityRef[] entities =
        [
            new(CampaignEntityKind.Faction, 2, CampaignEventEntityRole.Subject, "Tyranids"),
            new(CampaignEntityKind.Faction, 1, CampaignEventEntityRole.Related, "Space Marines"),
            new(CampaignEntityKind.Region, 6431, CampaignEventEntityRole.Location, "Grist Nine Pi"),
            new(CampaignEntityKind.Planet, 401, CampaignEventEntityRole.Location, "Grist Nine")
        ];
        CampaignEvent confirmed = new(1, CampaignEventType.FactionPresenceConfirmed, 5, 5, null, "confirmed", 1,
            new FactionIntelEventPayload(401, 6431, 1, 2, IntelLevel.Located, IntelLevel.Confirmed, 1f, false,
                CampaignEventType.FactionPresenceConfirmed),
            entities, CampaignEventPublication.ServiceRecordOnly());
        CampaignEvent firstContact = new(2, CampaignEventType.FactionFirstContact, 5, 5, null, "first-contact", 1,
            new FactionIntelEventPayload(401, 6431, 1, 2, IntelLevel.Located, IntelLevel.Confirmed, 1f, true,
                CampaignEventType.FactionFirstContact),
            entities, new CampaignEventPublication(CampaignEventSurfaceFlags.ChapterChronicle,
                CampaignEventImportance.Notable, CampaignEventReasonFlags.FirstContact,
                CampaignEventChronicleTreatment.Standalone, CampaignEventClassifier.CurrentVersion));

        ChapterChronicleEntry entry = new ChapterChronicleComposer().Compose(
            1, [firstContact], earlierEvents: new List<CampaignEvent> { confirmed });

        Assert.Equal("First Contact with the Tyranids", entry.Title);
        Assert.Contains("the measure of the Tyranids", entry.Body);
        Assert.Empty(entry.CallbackEventIds);
        Assert.DoesNotContain("recalled an earlier entry", entry.Body);
    }

    [Fact]
    public void ASoldierInAnotherRoleIsNotACallbackLink()
    {
        CampaignEvent masterHistory = new(1, CampaignEventType.MentorAssigned, 1, 1, null, "mentor", 3,
            new MentorAssignedPayload(MentorRelationshipKind.ScoutMentor,
                MentorAssignmentContext.NeophytePlacement, 5, "Scout Squad IX", 8, "Sergeant Decimus"),
            [new(CampaignEntityKind.Soldier, 973, CampaignEventEntityRole.Subject, "Keiroc Rostovsky")],
            CampaignEventPublication.ServiceRecordOnly());
        CampaignEvent founded = new(2, CampaignEventType.ChapterFounded, 2, 2, null, "chapter/founded", 1,
            new ChapterFoundedPayload("Test", 2, 973, "Keiroc Rostovsky", 1000, "Lord", "War.", 401, "Grist Nine"),
            [
                new(CampaignEntityKind.Chapter, 0, CampaignEventEntityRole.Subject, "Test"),
                new(CampaignEntityKind.Soldier, 973, CampaignEventEntityRole.Authority, "Keiroc Rostovsky")
            ],
            new CampaignEventPublication(CampaignEventSurfaceFlags.ChapterChronicle,
                CampaignEventImportance.Defining, CampaignEventReasonFlags.None,
                CampaignEventChronicleTreatment.Standalone, CampaignEventClassifier.CurrentVersion));

        Assert.Empty(ChronicleContinuitySelector.Select(founded, [founded], [masterHistory], 2));
    }
}
