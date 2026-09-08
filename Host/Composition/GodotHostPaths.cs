using Godot;
using OnlyWar.Abstractions;
using OnlyWar.Application;
using OnlyWar.Helpers.Settings;
using OnlyWar.Helpers.Storage;

namespace OnlyWar.Host.Composition;

public static class GodotHostPaths
{
    public static GameStorage CreateStorage() =>
        new(ProjectSettings.GlobalizePath("user://saves"));

    public static CampaignServices CreateCampaignServices(IRNG random) =>
        new(random, CreateStorage());

    public static CampaignServices CreateCampaignServices(IRNG random, GameStorage storage) =>
        new(random, storage);

    public static IEndTurnWarningPreferencesRepository CreateWarningPreferences() =>
        new EndTurnWarningPreferencesRepository(
            ProjectSettings.GlobalizePath(EndTurnWarningPreferencesRepository.DefaultUserPath));
}
