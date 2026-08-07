using OnlyWar.Helpers;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Builders
{
    public static class SquadFactory
    {
        public static Squad GenerateSquad(SquadTemplate squadTemplate, IRNG random, string name = "")
        {
            return GenerateSquad(squadTemplate, random, null, name);
        }

        public static Squad GenerateSquad(
            SquadTemplate squadTemplate,
            IRNG random,
            IEntityIdAllocator entityIds,
            string name = "")
        {
            Dictionary<SquadTemplateElement, int> counts = squadTemplate.Elements
                .ToDictionary(element => element, element => (int)element.MaximumNumber);
            return GenerateSquad(squadTemplate, counts, random, entityIds, name);
        }

        public static Squad GenerateSquadWithinBudget(
            SquadTemplate squadTemplate,
            long maximumBattleValue,
            IRNG random,
            string name = "")
        {
            return GenerateSquadWithinBudget(
                squadTemplate,
                maximumBattleValue,
                random,
                null,
                name);
        }

        public static Squad GenerateSquadWithinBudget(
            SquadTemplate squadTemplate,
            long maximumBattleValue,
            IRNG random,
            IEntityIdAllocator entityIds,
            string name = "")
        {
            Dictionary<SquadTemplateElement, int> counts = CalculateSquadCountsWithinBudget(
                squadTemplate,
                maximumBattleValue,
                out long battleValue);
            if (counts == null || battleValue <= 0) return null;

            return GenerateSquad(squadTemplate, counts, random, entityIds, name);
        }

        public static long CalculateSquadBattleValueWithinBudget(SquadTemplate squadTemplate, long maximumBattleValue)
        {
            CalculateSquadCountsWithinBudget(squadTemplate, maximumBattleValue, out long battleValue);
            return battleValue;
        }

        private static Dictionary<SquadTemplateElement, int> CalculateSquadCountsWithinBudget(
            SquadTemplate squadTemplate,
            long maximumBattleValue,
            out long battleValue)
        {
            battleValue = 0;
            if (squadTemplate == null || maximumBattleValue <= 0) return null;

            Dictionary<SquadTemplateElement, int> counts = [];
            foreach (SquadTemplateElement element in squadTemplate.Elements)
            {
                long requiredBattleValue = (long)element.MinimumNumber * element.SoldierTemplate.BattleValue;
                if (battleValue + requiredBattleValue > maximumBattleValue) return null;

                counts[element] = element.MinimumNumber;
                battleValue += requiredBattleValue;
            }

            bool addedSoldier;
            do
            {
                addedSoldier = false;
                foreach (SquadTemplateElement element in squadTemplate.Elements.OrderBy(e => e.SoldierTemplate.BattleValue))
                {
                    if (counts[element] >= element.MaximumNumber) continue;
                    int soldierBattleValue = element.SoldierTemplate.BattleValue;
                    if (soldierBattleValue <= 0 || battleValue + soldierBattleValue > maximumBattleValue) continue;

                    counts[element]++;
                    battleValue += soldierBattleValue;
                    addedSoldier = true;
                }
            }
            while (addedSoldier);

            if (counts.Values.Sum() == 0 || battleValue <= 0) return null;
            return counts;
        }

        private static Squad GenerateSquad(
            SquadTemplate squadTemplate,
            IReadOnlyDictionary<SquadTemplateElement, int> counts,
            IRNG random,
            IEntityIdAllocator entityIds,
            string name)
        {
            Squad squad = entityIds == null
                ? new Squad(name, null, squadTemplate)
                : new Squad(entityIds.GetNextId(), name, null, squadTemplate);
            foreach (SquadTemplateElement element in squadTemplate.Elements)
            {
                SoldierTemplate template = element.SoldierTemplate;
                Soldier[] soldiers = SoldierFactory.Instance.GenerateNewSoldiers(
                    counts[element],
                    template,
                    random,
                    entityIds);

                foreach (Soldier soldier in soldiers)
                {
                    squad.AddSquadMember(soldier);
                    soldier.AssignedSquad = squad;
                    soldier.Template = template;
                    soldier.Name = entityIds == null
                        ? $"{soldier.Template.Name} {soldier.Id}"
                        : soldier.Template.Name;
                }
            }
            // Pooled special-weapon quotas: for every element's quota group other than Command
            // Weapon (the individually-equipped groups CharacterLoadoutService owns instead —
            // see that class), roll how many of the group's menu get drafted into the squad's
            // pooled loadout, then roll which set fills each slot.
            //
            // A degenerate quota consumes no randomness. A fixed count (min == max) and a one-item
            // menu are both foregone conclusions, and asking the RNG a question with one possible
            // answer still advances the shared stream — so a crew-served slot like Brood Brother
            // Weapon Squad's single autocannon would shift every later draw in a seeded walk while
            // contributing no variation of its own. Skipping those calls costs nothing in outcome
            // and keeps the stream position meaningful. Note this is a deliberate departure from
            // the pre-migration squad-level SquadTemplateWeaponOption walk, which always drew:
            // seeded generation diverges for any squad holding such a quota.
            foreach (SquadTemplateElement element in squadTemplate.Elements)
            {
                foreach (SquadTemplateElementQuota quota in element.Quotas)
                {
                    if (quota.OptionGroup == CharacterLoadoutService.CommandWeaponGroup) continue;
                    IReadOnlyList<WeaponSet> menu = element.GetMenu(quota.OptionGroup);
                    if (menu.Count == 0) continue;

                    int taking = quota.MinimumRequired == quota.MaximumAllowed
                        ? quota.MinimumRequired
                        : random.GetIntBelowMax(quota.MinimumRequired, quota.MaximumAllowed + 1);
                    taking = System.Math.Min(taking, squad.Members.Count);
                    for (int i = 0; i < taking; i++)
                    {
                        squad.Loadout.Add(
                            menu.Count == 1 ? menu[0] : menu[random.GetIntBelowMax(0, menu.Count)]);
                    }
                }
            }
            return squad;
        }
    }
}
