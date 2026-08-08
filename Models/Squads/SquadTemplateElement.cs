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

        /// <summary>
        /// True when this slot musters at a strength rolled in [Min, Max] rather than always at
        /// Max. This is deliberately NOT inferred from Min &lt; Max: most ranged elements in the
        /// rules data (a Tactical Squad's 4-9 marines, a chapter office's 0-50) express an
        /// establishment the squad is filled TO and a floor below which it is understrength, and
        /// generation has always built them at Max. Rolled strength is the irregular-formation
        /// case — an insurrectionist mob that turns out however many turn out — and every element
        /// that wants it must say so.
        /// </summary>
        public bool RollsStrength { get; }

        /// <summary>
        /// Average strength of this slot, used to price the template for force generation. Equals
        /// <see cref="MaximumNumber"/> unless the slot rolls, so pricing is unchanged for every
        /// element authored before rolled strength existed.
        /// </summary>
        public float ExpectedNumber =>
            RollsStrength ? (MinimumNumber + MaximumNumber) / 2.0f : MaximumNumber;

        public SquadTemplateElement(
            SoldierTemplate soldierTemplate,
            byte minNumber,
            byte maxNumber,
            int id = 0,
            WeaponSet defaultWeapons = null,
            IReadOnlyList<SquadTemplateElementQuota> quotas = null,
            bool rollsStrength = false)
        {
            Id = id;
            SoldierTemplate = soldierTemplate;
            MinimumNumber = minNumber;
            MaximumNumber = maxNumber;
            DefaultWeapons = defaultWeapons;
            Quotas = quotas ?? [];
            RollsStrength = rollsStrength && maxNumber > minNumber;
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
