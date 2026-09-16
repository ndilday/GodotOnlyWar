using System.Linq;
using OnlyWar.Application.Battles;
using OnlyWar.Battles;
using OnlyWar.Domain;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Squads;
using OnlyWar.Persistence.Database.GameRules;
using OnlyWar.Runtime.Factories;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Application;

public class CampaignBattleEquipmentSourceTests
{
    [Fact]
    public void BattleSquad_AuthoredWeaponOnlyRoleKit_UsesSquadDefaultArmor()
    {
        GameRulesData rules = GameRulesLoader.Load(RulesDatabaseFixture.DatabasePath);
        SquadTemplate template = rules.PlayerFaction.SquadTemplates.Values
            .First(candidate => candidate.Elements.Any(element => element.PersonalEquipmentRole != null));
        SquadTemplateElement personalElement = template.Elements
            .First(element => element.PersonalEquipmentRole != null);
        Squad squad = SquadFactory.GenerateSquad(template, new FixedRNG());
        CampaignBattleEquipmentSource equipment = new(rules, force: null);

        BattleSquad battleSquad = new(false, squad, equipment: equipment);
        BattleSoldier battleSoldier = battleSquad.Soldiers
            .Single(soldier => soldier.Soldier.Template == personalElement.SoldierTemplate);

        Assert.NotNull(battleSoldier.Armor);
        Assert.Equal(template.Armor.ArmorProvided, battleSoldier.Armor.Template.ArmorProvided);
        Assert.Equal(
            EquipmentRulesCatalog.GetArmorEquipmentId(template.Armor.Id),
            equipment.Resolve(battleSoldier.Soldier, squad, functioningHands: 2).Loadout.Armor.Id);
    }
}
