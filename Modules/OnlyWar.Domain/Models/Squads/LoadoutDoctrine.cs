using OnlyWar.Models.Equippables;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Squads
{
    /// <summary>
    /// Sparse, template-keyed loadout policy. An absent entry means that this scope inherits
    /// from the next broader scope; a present empty entry deliberately selects the template's
    /// standard weapons for every member.
    /// </summary>
    public sealed class LoadoutDoctrine
    {
        private readonly Dictionary<int, List<WeaponSet>> _loadouts = [];

        public IReadOnlyDictionary<int, List<WeaponSet>> Loadouts => _loadouts;

        public bool TryGetLoadout(int squadTemplateId, out IReadOnlyList<WeaponSet> loadout)
        {
            if (_loadouts.TryGetValue(squadTemplateId, out List<WeaponSet> stored))
            {
                loadout = stored;
                return true;
            }

            loadout = null;
            return false;
        }

        public void SetLoadout(int squadTemplateId, IEnumerable<WeaponSet> loadout)
        {
            _loadouts[squadTemplateId] = loadout?.ToList() ?? [];
        }

        public bool RemoveLoadout(int squadTemplateId) => _loadouts.Remove(squadTemplateId);

        public void ReplaceWith(LoadoutDoctrine source)
        {
            _loadouts.Clear();
            if (source == null) return;

            foreach ((int templateId, List<WeaponSet> loadout) in source._loadouts)
            {
                _loadouts[templateId] = loadout.ToList();
            }
        }
    }
}
