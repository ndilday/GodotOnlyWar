using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public enum LoadoutDoctrineSource
    {
        Custom,
        Planet,
        Chapter,
        Template
    }

    public sealed record EffectiveLoadout(
        IReadOnlyList<WeaponSet> WeaponSets,
        LoadoutDoctrineSource Source,
        Planet Planet = null);

    public static class LoadoutDoctrineService
    {
        public static EffectiveLoadout Resolve(Squad squad, PlayerForce playerForce)
        {
            if (squad == null)
            {
                return new EffectiveLoadout([], LoadoutDoctrineSource.Template);
            }

            if (!squad.UsesLoadoutDoctrine || squad.Faction?.IsPlayerFaction != true || playerForce == null)
            {
                return new EffectiveLoadout(squad.Loadout ?? [], LoadoutDoctrineSource.Custom);
            }

            Planet planet = GetPlanet(squad);
            if (planet?.LoadoutDoctrine.TryGetLoadout(
                    squad.SquadTemplate.Id, out IReadOnlyList<WeaponSet> planetLoadout) == true)
            {
                return new EffectiveLoadout(planetLoadout, LoadoutDoctrineSource.Planet, planet);
            }

            if (playerForce.Army.LoadoutDoctrine.TryGetLoadout(
                    squad.SquadTemplate.Id, out IReadOnlyList<WeaponSet> chapterLoadout))
            {
                return new EffectiveLoadout(chapterLoadout, LoadoutDoctrineSource.Chapter);
            }

            // The template default is implicit: BattleSquad fills every unallocated member with
            // SquadTemplate.DefaultWeapons, so the explicit special-weapon list is empty.
            return new EffectiveLoadout([], LoadoutDoctrineSource.Template);
        }

        public static EffectiveLoadout Resolve(Squad squad)
        {
            PlayerForce force = GameDataSingleton.Instance?.Sector?.PlayerForce;
            return Resolve(squad, force);
        }

        public static IReadOnlyList<WeaponSet> GetEffectiveLoadout(Squad squad) =>
            Resolve(squad).WeaponSets;

        public static void Customize(Squad squad, PlayerForce playerForce = null)
        {
            if (squad == null || !squad.UsesLoadoutDoctrine) return;

            playerForce ??= GameDataSingleton.Instance?.Sector?.PlayerForce;
            squad.Loadout = Resolve(squad, playerForce).WeaponSets.ToList();
            squad.UsesLoadoutDoctrine = false;
        }

        public static void ReturnToDoctrine(Squad squad)
        {
            if (squad == null) return;
            squad.Loadout = [];
            squad.UsesLoadoutDoctrine = true;
        }

        public static Planet GetPlanet(Squad squad) =>
            squad?.CurrentRegion?.Planet ?? squad?.BoardedLocation?.Fleet?.Planet;

        public static string DescribeSource(EffectiveLoadout effective) => effective.Source switch
        {
            LoadoutDoctrineSource.Custom => "Custom loadout",
            LoadoutDoctrineSource.Planet => $"Following {effective.Planet?.Name ?? "planet"} theater doctrine",
            LoadoutDoctrineSource.Chapter => "Following chapter doctrine",
            _ => "Following squad-template standard"
        };
    }
}
