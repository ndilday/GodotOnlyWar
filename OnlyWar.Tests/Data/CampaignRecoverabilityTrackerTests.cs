using OnlyWar.Helpers.Storage;
using Xunit;

namespace OnlyWar.Tests.Data;

public sealed class CampaignRecoverabilityTrackerTests
{
    [Fact]
    public void NewCampaign_RemainsDirtyUntilCurrentRevisionSavesSuccessfully()
    {
        CampaignRecoverabilityTracker tracker = new();
        tracker.BeginNewCampaign();
        CampaignRevision foundingRevision = tracker.CaptureRevision();

        Assert.True(tracker.IsDirty);
        Assert.False(tracker.HasRecoverableState);

        tracker.MarkSaveSucceeded(foundingRevision);

        Assert.False(tracker.IsDirty);
        Assert.True(tracker.HasRecoverableState);
        Assert.Equal(foundingRevision, tracker.RecoverableRevision);

        tracker.MarkChanged();
        Assert.True(tracker.IsDirty);
    }

    [Fact]
    public void SaveOfEarlierRevision_DoesNotClearLaterMutation()
    {
        CampaignRecoverabilityTracker tracker = new();
        tracker.BeginLoadedCampaign();
        CampaignRevision revisionBeingSaved = tracker.MarkChanged();

        tracker.MarkChanged();
        tracker.MarkSaveSucceeded(revisionBeingSaved);

        Assert.True(tracker.IsDirty);
        Assert.Equal(revisionBeingSaved, tracker.RecoverableRevision);
    }

    [Fact]
    public void LoadedCampaign_StartsCleanAndFailureRequiresNoStateChange()
    {
        CampaignRecoverabilityTracker tracker = new();
        tracker.BeginLoadedCampaign();

        Assert.False(tracker.IsDirty);
        Assert.True(tracker.HasRecoverableState);

        CampaignRevision changed = tracker.MarkChanged();
        Assert.True(tracker.IsDirty);

        // A failed save simply omits MarkSaveSucceeded.
        Assert.NotEqual(changed, tracker.RecoverableRevision);
        Assert.True(tracker.IsDirty);
    }

    [Fact]
    public void LateCompletionOfOlderSave_DoesNotReplaceNewerRecoveryPoint()
    {
        CampaignRecoverabilityTracker tracker = new();
        tracker.BeginLoadedCampaign();
        CampaignRevision older = tracker.MarkChanged();
        CampaignRevision newer = tracker.MarkChanged();

        tracker.MarkSaveSucceeded(newer);
        tracker.MarkSaveSucceeded(older);

        Assert.False(tracker.IsDirty);
        Assert.Equal(newer, tracker.RecoverableRevision);
    }
}
