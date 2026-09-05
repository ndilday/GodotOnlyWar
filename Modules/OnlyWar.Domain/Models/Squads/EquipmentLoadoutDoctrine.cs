using OnlyWar.Models.Equippables;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Squads
{
    /// <summary>
    /// Sparse persistent overrides for the itemized equipment model. Missing entries mean inherit;
    /// the stored values themselves are complete loadouts so a later role-default edit cannot
    /// silently mutate a soldier's bespoke composition.
    /// </summary>
    public sealed class EquipmentLoadoutDoctrine
    {
        private readonly Dictionary<int, EquipmentLoadout> _roleDefaults = [];
        private readonly Dictionary<int, EquipmentLoadout> _personalLoadouts = [];

        public IReadOnlyDictionary<int, EquipmentLoadout> RoleDefaults => _roleDefaults;
        public IReadOnlyDictionary<int, EquipmentLoadout> PersonalLoadouts => _personalLoadouts;

        public bool TryGetRoleDefault(int roleId, out EquipmentLoadout loadout) =>
            _roleDefaults.TryGetValue(roleId, out loadout);

        public bool TryGetPersonalLoadout(int soldierId, out EquipmentLoadout loadout) =>
            _personalLoadouts.TryGetValue(soldierId, out loadout);

        public void SetRoleDefault(int roleId, EquipmentLoadout loadout)
        {
            if (roleId <= 0) throw new ArgumentOutOfRangeException(nameof(roleId));
            _roleDefaults[roleId] = loadout ?? throw new ArgumentNullException(nameof(loadout));
        }

        public void SetPersonalLoadout(int soldierId, EquipmentLoadout loadout)
        {
            if (soldierId <= 0) throw new ArgumentOutOfRangeException(nameof(soldierId));
            _personalLoadouts[soldierId] = loadout ?? throw new ArgumentNullException(nameof(loadout));
        }

        public void ClearRoleDefault(int roleId) => _roleDefaults.Remove(roleId);
        public void ClearPersonalLoadout(int soldierId) => _personalLoadouts.Remove(soldierId);

        public void ReplaceWith(EquipmentLoadoutDoctrine source)
        {
            _roleDefaults.Clear();
            _personalLoadouts.Clear();
            if (source == null)
            {
                return;
            }

            foreach ((int roleId, EquipmentLoadout loadout) in source.RoleDefaults)
            {
                SetRoleDefault(roleId, loadout);
            }
            foreach ((int soldierId, EquipmentLoadout loadout) in source.PersonalLoadouts)
            {
                SetPersonalLoadout(soldierId, loadout);
            }
        }

        public EquipmentLoadoutDoctrine DeepCopy()
        {
            EquipmentLoadoutDoctrine copy = new();
            foreach ((int roleId, EquipmentLoadout loadout) in _roleDefaults)
            {
                copy.SetRoleDefault(roleId, loadout);
            }
            foreach ((int soldierId, EquipmentLoadout loadout) in _personalLoadouts)
            {
                copy.SetPersonalLoadout(soldierId, loadout);
            }
            return copy;
        }
    }

    public enum EquipmentLoadoutSource
    {
        Personal,
        ChapterRole,
        AuthoredRole,
        ElementFallback,
        SquadFallback,
        Pooled
    }

    public sealed record ResolvedEquipmentLoadout(
        EquipmentLoadout Loadout,
        EquipmentLoadoutSource Source,
        PersonalEquipmentRole Role,
        bool IsActivePersonalOverride,
        IReadOnlyList<EquipmentValidationIssue> ValidationIssues);
}
