using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using System.Drawing;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class LoadoutDoctrineServiceTests
{
    private readonly WeaponSet _chapterSet = new(701, "Chapter set");
    private readonly WeaponSet _planetSet = new(702, "Planet set");
    private readonly WeaponSet _customSet = new(703, "Custom set");

    [Fact]
    public void Resolve_FollowerOnConfiguredPlanet_PrefersPlanetOverChapter()
    {
        (Squad squad, PlayerForce force, Planet planet) = CreateContext();
        force.Army.LoadoutDoctrine.SetLoadout(squad.SquadTemplate.Id, [_chapterSet]);
        planet.LoadoutDoctrine.SetLoadout(squad.SquadTemplate.Id, [_planetSet]);

        EffectiveLoadout effective = LoadoutDoctrineService.Resolve(squad, force);

        Assert.Equal(LoadoutDoctrineSource.Planet, effective.Source);
        Assert.Same(_planetSet, Assert.Single(effective.WeaponSets));
    }

    [Fact]
    public void Resolve_PresentEmptyPlanetOverride_DoesNotFallThroughToChapter()
    {
        (Squad squad, PlayerForce force, Planet planet) = CreateContext();
        force.Army.LoadoutDoctrine.SetLoadout(squad.SquadTemplate.Id, [_chapterSet]);
        planet.LoadoutDoctrine.SetLoadout(squad.SquadTemplate.Id, []);

        EffectiveLoadout effective = LoadoutDoctrineService.Resolve(squad, force);

        Assert.Equal(LoadoutDoctrineSource.Planet, effective.Source);
        Assert.Empty(effective.WeaponSets);
    }

    [Fact]
    public void CustomizeAndReturnToDoctrine_PreservesExceptionThenRestoresInheritance()
    {
        (Squad squad, PlayerForce force, Planet planet) = CreateContext();
        planet.LoadoutDoctrine.SetLoadout(squad.SquadTemplate.Id, [_planetSet]);

        LoadoutDoctrineService.Customize(squad, force);
        squad.Loadout = [_customSet];
        Assert.Equal(LoadoutDoctrineSource.Custom, LoadoutDoctrineService.Resolve(squad, force).Source);
        Assert.Same(_customSet, Assert.Single(LoadoutDoctrineService.Resolve(squad, force).WeaponSets));

        LoadoutDoctrineService.ReturnToDoctrine(squad);
        EffectiveLoadout restored = LoadoutDoctrineService.Resolve(squad, force);
        Assert.Equal(LoadoutDoctrineSource.Planet, restored.Source);
        Assert.Same(_planetSet, Assert.Single(restored.WeaponSets));
    }

    private static (Squad Squad, PlayerForce Force, Planet Planet) CreateContext()
    {
        WeaponSet standard = new(700, "Standard set");
        SquadTemplate template = new(501, "Tactical Squad", standard, [], null, [], SquadTypes.None);
        Faction faction = new(
            11, "Player", Color.Black, true, false, false, GrowthType.None,
            null, null, new Dictionary<int, SquadTemplate> { [template.Id] = template },
            null, null, null, null);
        Squad squad = new(601, "Alpha", null, template);
        Army army = new("Army", null, null, null, []);
        PlayerForce force = new(faction, army, new Fleet("Fleet", null, null));
        Planet planet = new(801, "Theater", new Coordinate(1, 1), 16, null, 1, 0);
        Region region = new(901, planet, 0, "Landing Zone", new RegionCoordinate(0, 0), 0);
        planet.Regions[0] = region;
        squad.CurrentRegion = region;
        return (squad, force, planet);
    }
}
