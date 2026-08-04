using OnlyWar.Models.Equippables;
using System.Collections.Generic;

namespace OnlyWar.Models.Squads
{
    /// <summary>
    /// Loadout policy for characters — command staff and specialists the player equips
    /// individually rather than through a squad's pooled weapon counts.
    ///
    /// Two sparse layers, both absent-means-inherit like <see cref="LoadoutDoctrine"/>:
    /// role defaults keyed by SoldierTemplate.Id ("every Chaplain carries a crozius"), and
    /// personal loadouts keyed by Soldier.Id for the individuals worth hand-equipping.
    ///
    /// There is deliberately no per-planet layer. The theater tier exists so the player need not
    /// hand-edit a hundred interchangeable line squads; a chapter fields a few dozen characters,
    /// each unique, so the individual layer already covers that need.
    /// </summary>
    public sealed class CharacterLoadoutDoctrine
    {
        private readonly Dictionary<int, WeaponSet> _roleDefaults = [];
        private readonly Dictionary<int, WeaponSet> _personalLoadouts = [];

        /// <summary>Chapter-wide default per soldier template, keyed by SoldierTemplate.Id.</summary>
        public IReadOnlyDictionary<int, WeaponSet> RoleDefaults => _roleDefaults;

        /// <summary>Per-individual overrides, keyed by Soldier.Id.</summary>
        public IReadOnlyDictionary<int, WeaponSet> PersonalLoadouts => _personalLoadouts;

        public bool TryGetRoleDefault(int soldierTemplateId, out WeaponSet weaponSet) =>
            _roleDefaults.TryGetValue(soldierTemplateId, out weaponSet);

        public void SetRoleDefault(int soldierTemplateId, WeaponSet weaponSet)
        {
            if (weaponSet == null)
            {
                _roleDefaults.Remove(soldierTemplateId);
                return;
            }
            _roleDefaults[soldierTemplateId] = weaponSet;
        }

        public bool ClearRoleDefault(int soldierTemplateId) => _roleDefaults.Remove(soldierTemplateId);

        public bool TryGetPersonalLoadout(int soldierId, out WeaponSet weaponSet) =>
            _personalLoadouts.TryGetValue(soldierId, out weaponSet);

        public void SetPersonalLoadout(int soldierId, WeaponSet weaponSet)
        {
            if (weaponSet == null)
            {
                _personalLoadouts.Remove(soldierId);
                return;
            }
            _personalLoadouts[soldierId] = weaponSet;
        }

        public bool ClearPersonalLoadout(int soldierId) => _personalLoadouts.Remove(soldierId);

        public void ReplaceWith(CharacterLoadoutDoctrine source)
        {
            _roleDefaults.Clear();
            _personalLoadouts.Clear();
            if (source == null) return;

            foreach ((int templateId, WeaponSet weaponSet) in source._roleDefaults)
            {
                _roleDefaults[templateId] = weaponSet;
            }
            foreach ((int soldierId, WeaponSet weaponSet) in source._personalLoadouts)
            {
                _personalLoadouts[soldierId] = weaponSet;
            }
        }
    }
}
