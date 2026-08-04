using System.Collections.Generic;
using System.Linq;

using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Models.Squads
{
    /// <summary>
    /// How many bodies of a <see cref="SquadTemplateElement"/> may draw from one of its
    /// SoldierTemplate's named weapon-set menus, and within what range. The menu itself (which
    /// sets exist) belongs to the SoldierTemplate — same wherever the role stands; the quota
    /// (how many bodies here may draw from it) belongs to the element, so a Devastator Squad and
    /// a Tactical Squad can field the same SoldierTemplate under different limits.
    /// </summary>
    public sealed class SquadTemplateElementQuota
    {
        public string OptionGroup { get; }
        public int MinimumRequired { get; }
        public int MaximumAllowed { get; }

        public SquadTemplateElementQuota(string optionGroup, int minimumRequired, int maximumAllowed)
        {
            OptionGroup = optionGroup;
            MinimumRequired = minimumRequired;
            MaximumAllowed = maximumAllowed;
        }
    }

    public class SquadTemplateElement
    {
        public int Id { get; }
        public SoldierTemplate SoldierTemplate { get; }
        public byte MinimumNumber { get; }
        public byte MaximumNumber { get; }
        /// <summary>
        /// What this slot's bodies carry absent any menu choice. Null falls back to
        /// SquadTemplate.DefaultWeapons.
        /// </summary>
        public WeaponSet DefaultWeapons { get; }
        public IReadOnlyList<SquadTemplateElementQuota> Quotas { get; }

        public SquadTemplateElement(
            SoldierTemplate soldierTemplate,
            byte minNumber,
            byte maxNumber,
            int id = 0,
            WeaponSet defaultWeapons = null,
            IReadOnlyList<SquadTemplateElementQuota> quotas = null)
        {
            Id = id;
            SoldierTemplate = soldierTemplate;
            MinimumNumber = minNumber;
            MaximumNumber = maxNumber;
            DefaultWeapons = defaultWeapons;
            Quotas = quotas ?? [];
        }

        public bool TryGetQuota(string optionGroup, out SquadTemplateElementQuota quota)
        {
            quota = Quotas.FirstOrDefault(q => q.OptionGroup == optionGroup);
            return quota != null;
        }

        /// <summary>
        /// The weapon-set menu for one of this element's quota groups, drawn from the element's
        /// own SoldierTemplate. Empty if the role authors no menu for that group.
        /// </summary>
        public IReadOnlyList<WeaponSet> GetMenu(string optionGroup) =>
            SoldierTemplate?.GetWeaponOptions(optionGroup) ?? [];

    }
}
