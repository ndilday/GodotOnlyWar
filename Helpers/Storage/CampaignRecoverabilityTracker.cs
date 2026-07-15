using System;

namespace OnlyWar.Helpers.Storage
{
    internal readonly record struct CampaignRevision(long Value);

    /// <summary>
    /// Tracks whether the current in-memory revision has a successful recovery point. A caller
    /// captures the revision before writing and reports success afterward; if state changed while
    /// the write was in progress, the newer revision correctly remains dirty.
    /// </summary>
    internal sealed class CampaignRecoverabilityTracker
    {
        private long _currentRevision;
        private long? _recoverableRevision;

        internal event EventHandler StateChanged;

        internal CampaignRevision CurrentRevision => new(_currentRevision);
        internal CampaignRevision? RecoverableRevision => _recoverableRevision.HasValue
            ? new CampaignRevision(_recoverableRevision.Value)
            : null;
        internal bool HasRecoverableState => _recoverableRevision.HasValue;
        internal bool IsDirty => !_recoverableRevision.HasValue
            || _recoverableRevision.Value != _currentRevision;

        internal void BeginNewCampaign()
        {
            SetState(1, null);
        }

        internal void BeginLoadedCampaign()
        {
            SetState(0, 0);
        }

        internal CampaignRevision MarkChanged()
        {
            checked
            {
                _currentRevision++;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
            return CurrentRevision;
        }

        internal CampaignRevision CaptureRevision()
        {
            return CurrentRevision;
        }

        internal void MarkSaveSucceeded(CampaignRevision savedRevision)
        {
            if (savedRevision.Value < 0 || savedRevision.Value > _currentRevision)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(savedRevision),
                    "The saved revision must belong to the current campaign state.");
            }

            long? previousRecoverableRevision = _recoverableRevision;
            _recoverableRevision = _recoverableRevision.HasValue
                ? Math.Max(_recoverableRevision.Value, savedRevision.Value)
                : savedRevision.Value;
            if (previousRecoverableRevision != _recoverableRevision)
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void SetState(long currentRevision, long? recoverableRevision)
        {
            _currentRevision = currentRevision;
            _recoverableRevision = recoverableRevision;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
