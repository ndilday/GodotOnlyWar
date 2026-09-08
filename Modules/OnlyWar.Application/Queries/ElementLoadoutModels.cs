using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Squads;

namespace OnlyWar.Application
{
    /// <summary>
    /// One character-equipped slot. Key is a soldier id when editing a live squad's personal
    /// loadouts, or a personal-equipment-role id when editing a chapter-wide role default —
    /// callers interpret it, the editor only echoes it back on the change events.
    /// </summary>
    public sealed record CharacterLoadoutRowData(
        int Key,
        string Title,
        string Detail,
        IReadOnlyList<WeaponSet> Options,
        WeaponSet Selected,
        bool CanReset);

    /// <summary>
    /// One pooled (MaximumAllowed &gt; 1) quota group on an element — "up to 4 heavies" — sharing
    /// the element's standard-issue capacity with any sibling groups on the same element.
    /// </summary>
    public sealed record CountGroupData(
        string OptionGroup,
        IReadOnlyList<WeaponSet> Menu,
        int MinimumRequired,
        int MaximumAllowed);

    /// <summary>
    /// One element's count-based section: a standard-issue readout shared by every quota group on
    /// the element, each group rendered as its own spinbox row-set. Capacity is that element's own
    /// body count, supplied by the caller — a live squad's able-bodied roster for that element, or
    /// the template's MaximumNumber when there is no roster yet.
    /// </summary>
    public sealed record ElementCountSectionData(
        string StandardIssueName,
        int Capacity,
        IReadOnlyList<CountGroupData> Groups);

    /// <summary>
    /// Builds <see cref="ElementCountSectionData"/> from a template's elements, one section per
    /// element that has a pooled quota group. Shared by the squad screen (live roster capacity) and
    /// the doctrine dialog (template MaximumNumber capacity) so the per-element capacity scoping —
    /// never the whole squad's or the whole template's — lives in exactly one place.
    /// </summary>
    public static class ElementLoadoutSections
    {
        public static List<ElementCountSectionData> Build(
            SquadTemplate template,
            Func<SquadTemplateElement, int> capacityForElement,
            int? squadModelCount = null)
        {
            List<ElementCountSectionData> sections = [];
            if (template?.Elements == null) return sections;
            int totalModelCount = squadModelCount
                ?? template.Elements.Sum(element => (int)element.MaximumNumber);

            foreach (SquadTemplateElement element in template.Elements)
            {
                // An explicit PersonalEquipmentRole owns the element's complete composition and
                // never enters the pooled count editor. Legacy fixtures without that relation
                // retain the old Command Weapon split until their rules rows are migrated.
                List<CountGroupData> groups = element.PersonalEquipmentRole != null
                    ? []
                    : element.Quotas
                    .Where(quota => quota.OptionGroup != CharacterLoadoutService.CommandWeaponGroup)
                    .Select(quota => new CountGroupData(
                        quota.OptionGroup,
                        element.GetMenu(quota.OptionGroup),
                        quota.MinimumRequired,
                        quota.MaximumAllowed))
                    .ToList();
                // Elements with no pooled quota (fixed-loadout troopers, or Command-Weapon-only
                // slots handled by the character rows instead) contribute no section at all.
                if (groups.Count == 0) continue;

                string standardName = element.DefaultWeapons?.Name
                    ?? template.DefaultWeapons?.Name
                    ?? "Standard weapons";
                int elementCapacity = capacityForElement(element);
                sections.Add(new ElementCountSectionData(
                    standardName,
                    elementCapacity,
                    groups.Select(group =>
                    {
                        SquadTemplateElementQuota quota = element.Quotas
                            .First(candidate => candidate.OptionGroup == group.OptionGroup);
                        return group with
                        {
                            MaximumAllowed = quota.GetMaximumAllowed(elementCapacity, totalModelCount)
                        };
                    }).ToList()));
            }
            return sections;
        }
    }
}
