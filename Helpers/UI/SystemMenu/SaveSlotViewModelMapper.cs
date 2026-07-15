using OnlyWar.Helpers.Storage;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.UI.SystemMenu
{
    internal static class SaveSlotViewModelMapper
    {
        internal static IReadOnlyList<SaveSlotViewModel> Map(
            IEnumerable<SaveGameEntry> entries)
        {
            return (entries ?? Enumerable.Empty<SaveGameEntry>())
                .Select(entry => new SaveSlotViewModel(
                    entry.FilePath,
                    entry.FilePath,
                    entry.DisplayName,
                    entry.Kind == SaveGameKind.Manual
                        ? SaveSlotKind.Manual
                        : SaveSlotKind.Autosave,
                    entry.CampaignName,
                    entry.CampaignDate?.ToString(),
                    entry.LastWriteTimeLocal,
                    entry.IsCompatible,
                    entry.FailureReason))
                .ToList();
        }
    }
}
