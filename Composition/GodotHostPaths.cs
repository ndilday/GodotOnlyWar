using Godot;
using OnlyWar.Helpers.Settings;
using OnlyWar.Helpers.Storage;

namespace OnlyWar.Composition;

public static class GodotHostPaths
{
    public static void Configure() =>
        GameStorage.ConfigureSaveDirectory(ProjectSettings.GlobalizePath("user://saves"));

    public static EndTurnWarningPreferencesRepository CreateWarningPreferences() =>
        new(ProjectSettings.GlobalizePath(EndTurnWarningPreferencesRepository.DefaultUserPath));
}
