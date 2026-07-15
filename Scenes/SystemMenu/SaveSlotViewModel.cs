using System;

namespace OnlyWar.Helpers.UI.SystemMenu
{
    public enum SaveSlotKind
    {
        Manual,
        Autosave
    }

    /// <summary>
    /// Presentation-only save metadata. Storage code maps its catalog entries to this type so the
    /// chooser remains independent of the on-disk metadata implementation.
    /// </summary>
    public sealed record SaveSlotViewModel(
        string SlotId,
        string FilePath,
        string DisplayName,
        SaveSlotKind Kind,
        string ChapterName,
        string CampaignDate,
        DateTime LastWriteTime,
        bool IsCompatible,
        string StateDescription = null);

    public enum SaveChooserMode
    {
        Save,
        Load
    }

    public sealed class SaveSlotRequestedEventArgs : EventArgs
    {
        public SaveSlotRequestedEventArgs(string name, SaveSlotViewModel overwriteTarget)
        {
            Name = name;
            OverwriteTarget = overwriteTarget;
        }

        public string Name { get; }
        public SaveSlotViewModel OverwriteTarget { get; }
    }

    public sealed class SaveSlotSelectionEventArgs : EventArgs
    {
        public SaveSlotSelectionEventArgs(SaveSlotViewModel slot)
        {
            Slot = slot;
        }

        public SaveSlotViewModel Slot { get; }
    }
}
