using System;

namespace OnlyWar.Helpers.Storage
{
    public readonly record struct CampaignRevision(long Value);

    /// <summary>
    /// Tracks whether the current in-memory revision has a successful recovery point. A caller
    /// captures the revision before writing and reports success afterward; if state changed while
    /// the write was in progress, the newer revision correctly remains dirty.
    /// </summary>
    public sealed class CampaignRecoverabilityTracker
    {
        private long _currentRevision;
        private long? _recoverableRevision;

        public event EventHandler StateChanged;

        public CampaignRevision CurrentRevision => new(_currentRevision);
        public CampaignRevision? RecoverableRevision => _recoverableRevision.HasValue
            ? new CampaignRevision(_recoverableRevision.Value)
            : null;
        public bool HasRecoverableState => _recoverableRevision.HasValue;
        public bool IsDirty => !_recoverableRevision.HasValue
            || _recoverableRevision.Value != _currentRevision;

        public void BeginNewCampaign()
        {
            SetState(1, null);
        }

        public void BeginLoadedCampaign()
        {
            SetState(0, 0);
        }

        public CampaignRevision MarkChanged()
        {
            checked
            {
                _currentRevision++;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
            return CurrentRevision;
        }

        public CampaignRevision CaptureRevision()
        {
            return CurrentRevision;
        }

        public void MarkSaveSucceeded(CampaignRevision savedRevision)
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
