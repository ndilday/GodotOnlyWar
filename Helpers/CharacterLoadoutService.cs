using System.Linq;

using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Helpers
{
    public enum CharacterLoadoutSource
    {
        Personal,
        Chapter,
        Template
    }

    public sealed record EffectiveCharacterLoadout(
        WeaponSet WeaponSet,
        CharacterLoadoutSource Source);

    /// <summary>
    /// Resolves the weapon set an individual character carries. The squad-level
    /// <see cref="LoadoutDoctrineService"/> answers "what does this squad type field"; this
    /// answers "what does this man carry", which is a different question for anyone whose kit
    /// should not be handed out by whoever happens to have the best skill for it.
    ///
    /// "Character" is no longer a soldier-template flag: the same SoldierTemplate can be a lone
    /// specialist in one squad and a bulk instructor slot in another (the Chaplain in HQ Squad
    /// vs. the 10th Company HQ). What makes a body a character is his SquadTemplateElement
    /// carrying a <see cref="CommandWeaponGroup"/> quota, so resolution needs the soldier's
    /// squad (via <see cref="ISoldier.AssignedSquad"/>) to find his element.
    /// </summary>
    public static class CharacterLoadoutService
    {
        /// <summary>
        /// The one quota group <see cref="CharacterLoadoutDoctrine"/> can address — it stores a
        /// single role default per SoldierTemplate.Id and a single personal override per
        /// Soldier.Id, so the doctrine layers only ever apply to a soldier's Command Weapon
        /// quota. A pooled special-weapon group (Heavy Weapon, Support Weapon, ...) can hand its
        /// set to any of several interchangeable bodies and has no single "the" default to store
        /// against a template id; those stay on the skill-based pooled path in
        /// BattleSquad.AllocateEquipment, unchanged by this service.
        /// </summary>
        public const string CommandWeaponGroup = "Command Weapon";

        private static SquadTemplateElement FindElement(ISoldier soldier) =>
            soldier?.AssignedSquad?.SquadTemplate?.Elements
                .FirstOrDefault(e => e.SoldierTemplate == soldier.Template);

        public static bool IsCharacter(ISoldier soldier) =>
            FindElement(soldier) is SquadTemplateElement element
            && element.TryGetQuota(CommandWeaponGroup, out _);

        /// <summary>
        /// Personal override, else the chapter's default for the role, else the element's own
        /// authored default (falling back to the squad template's default). Returns null for
        /// anyone whose slot has no Command Weapon quota, which is the signal to callers that
        /// pooled squad allocation still applies.
        /// </summary>
        public static EffectiveCharacterLoadout Resolve(ISoldier soldier, PlayerForce playerForce)
        {
            SquadTemplateElement element = FindElement(soldier);
            if (element == null || !element.TryGetQuota(CommandWeaponGroup, out _))
            {
                return null;
            }

            CharacterLoadoutDoctrine doctrine = playerForce?.Army?.CharacterLoadoutDoctrine;
            if (doctrine != null)
            {
                if (doctrine.TryGetPersonalLoadout(soldier.Id, out WeaponSet personal))
                {
                    return new EffectiveCharacterLoadout(personal, CharacterLoadoutSource.Personal);
                }
                if (doctrine.TryGetRoleDefault(soldier.Template.Id, out WeaponSet roleDefault))
                {
                    return new EffectiveCharacterLoadout(roleDefault, CharacterLoadoutSource.Chapter);
                }
            }

            WeaponSet fallback = element.DefaultWeapons ?? soldier.AssignedSquad?.SquadTemplate?.DefaultWeapons;
            return new EffectiveCharacterLoadout(fallback, CharacterLoadoutSource.Template);
        }

        public static EffectiveCharacterLoadout Resolve(ISoldier soldier) =>
            Resolve(soldier, GameDataSingleton.Instance?.Sector?.PlayerForce);

        /// <summary>
        /// The set this soldier carries, or null if pooled squad allocation should equip him.
        /// </summary>
        public static WeaponSet GetEffectiveWeaponSet(ISoldier soldier) =>
            Resolve(soldier)?.WeaponSet;

        /// <summary>
        /// Resolves the chapter-wide kit for an element's role with no individual in mind — used
        /// by the doctrine editor, where the player is setting the default rather than equipping
        /// a man. Takes the element (not just the SoldierTemplate) because the default and the
        /// Command Weapon quota both live there now.
        /// </summary>
        public static EffectiveCharacterLoadout ResolveRole(
            SquadTemplateElement element, PlayerForce playerForce)
        {
            if (element == null || !element.TryGetQuota(CommandWeaponGroup, out _))
            {
                return null;
            }

            CharacterLoadoutDoctrine doctrine = playerForce?.Army?.CharacterLoadoutDoctrine;
            if (doctrine != null && doctrine.TryGetRoleDefault(element.SoldierTemplate.Id, out WeaponSet roleDefault))
            {
                return new EffectiveCharacterLoadout(roleDefault, CharacterLoadoutSource.Chapter);
            }
            return new EffectiveCharacterLoadout(element.DefaultWeapons, CharacterLoadoutSource.Template);
        }

        public static void SetPersonalLoadout(
            ISoldier soldier, WeaponSet weaponSet, PlayerForce playerForce = null)
        {
            if (!IsCharacter(soldier)) return;
            playerForce ??= GameDataSingleton.Instance?.Sector?.PlayerForce;
            playerForce?.Army?.CharacterLoadoutDoctrine.SetPersonalLoadout(soldier.Id, weaponSet);
        }

        public static void ClearPersonalLoadout(ISoldier soldier, PlayerForce playerForce = null)
        {
            if (soldier == null) return;
            playerForce ??= GameDataSingleton.Instance?.Sector?.PlayerForce;
            playerForce?.Army?.CharacterLoadoutDoctrine.ClearPersonalLoadout(soldier.Id);
        }

        public static void SetRoleDefault(
            SquadTemplateElement element, WeaponSet weaponSet, PlayerForce playerForce = null)
        {
            if (element?.TryGetQuota(CommandWeaponGroup, out _) != true) return;
            playerForce ??= GameDataSingleton.Instance?.Sector?.PlayerForce;
            playerForce?.Army?.CharacterLoadoutDoctrine.SetRoleDefault(element.SoldierTemplate.Id, weaponSet);
        }

        public static string DescribeSource(EffectiveCharacterLoadout effective) => effective?.Source switch
        {
            CharacterLoadoutSource.Personal => "Personal loadout",
            CharacterLoadoutSource.Chapter => "Chapter standard for this role",
            CharacterLoadoutSource.Template => "Role's standard kit",
            _ => ""
        };
    }
}
