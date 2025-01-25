using OnlyWar.Helpers;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Builders
{
    public static class SquadFactory
    {
        public static Squad GenerateSquad(SquadTemplate squadTemplate, string name = "")
        {
            Squad squad = new Squad(name, null, squadTemplate);
            foreach (SquadTemplateElement element in squadTemplate.Elements)
            {
                SoldierTemplate template = element.SoldierTemplate;
                Soldier[] soldiers = SoldierFactory.Instance.GenerateNewSoldiers(element.MaximumNumber, template);

                foreach (Soldier soldier in soldiers)
                {
                    squad.AddSquadMember(soldier);
                    soldier.AssignedSquad = squad;
                    soldier.Template = template;
                    soldier.Name = $"{soldier.Template.Name} {soldier.Id}";
                }
            }
            if (squad.SquadTemplate.WeaponOptions != null)
            {
                foreach (SquadWeaponOption weaponOption in squad.SquadTemplate.WeaponOptions)
                {
                    int taking = RNG.GetIntBelowMax(weaponOption.MinNumber, weaponOption.MaxNumber + 1);
                    int maxIndex = weaponOption.Options.Count;
                    for (int i = 0; i < taking; i++)
                    {
                        squad.Loadout.Add(weaponOption.Options[RNG.GetIntBelowMax(0, maxIndex)]);
                    }
                }
            }
            return squad;
        }
    }
}
