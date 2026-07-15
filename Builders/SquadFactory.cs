using OnlyWar.Helpers;
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
            if (squad.SquadTemplate.WeaponOptions != null)
            {
                foreach (SquadWeaponOption weaponOption in squad.SquadTemplate.WeaponOptions)
                {
                    int taking = random.GetIntBelowMax(
                        weaponOption.MinNumber,
                        weaponOption.MaxNumber + 1);
                    taking = System.Math.Min(taking, squad.Members.Count);
                    int maxIndex = weaponOption.Options.Count;
                    for (int i = 0; i < taking; i++)
                    {
                        squad.Loadout.Add(weaponOption.Options[random.GetIntBelowMax(0, maxIndex)]);
                    }
                }
            }
            return squad;
        }
    }
}
