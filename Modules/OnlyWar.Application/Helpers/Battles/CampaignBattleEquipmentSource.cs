using System;
using System.Linq;
using System.Collections.Generic;
using OnlyWar.Battles.Abstractions;
using OnlyWar.Domain;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;

namespace OnlyWar.Application.Battles;

public sealed class CampaignBattleEquipmentSource : IBattleEquipmentSource
{
    private readonly EquipmentRulesCatalog _catalog;
    private readonly EquipmentLoadoutDoctrine _doctrine;
    private readonly PlayerForce _force;
    public CampaignBattleEquipmentSource(GameRulesData rules, PlayerForce force, bool activeEquipment = true)
    {
        _catalog = activeEquipment ? rules?.EquipmentCatalog : null;
        _force = force;
        _doctrine = force?.Army?.EquipmentLoadoutDoctrine;
    }
    public bool UsesItemizedAttribution => _catalog != null;
    public IReadOnlyList<WeaponSet> GetSquadLoadout(Squad squad) => LoadoutDoctrineService.Resolve(squad, _force).WeaponSets;
    public WeaponSet GetCharacterWeapons(ISoldier soldier) => CharacterLoadoutService.Resolve(soldier, _force)?.WeaponSet;
    public ResolvedEquipmentLoadout Resolve(ISoldier soldier, Squad squad, int functioningHands)
    {
        var element = squad?.SquadTemplate?.Elements.FirstOrDefault(candidate => candidate.SoldierTemplate == soldier.Template);
        if (_catalog == null || element?.PersonalEquipmentRole == null) return null;
        var authored = _catalog.EquipmentKits.GetValueOrDefault(element.PersonalEquipmentRole.DefaultKitId);
        var elementFallback = element.DefaultWeapons == null ? null : _catalog.EquipmentKits.GetValueOrDefault(EquipmentRulesCatalog.GetKitId(element.DefaultWeapons.Id));
        var squadFallback = squad.SquadTemplate.DefaultWeapons == null ? null : _catalog.EquipmentKits.GetValueOrDefault(EquipmentRulesCatalog.GetKitId(squad.SquadTemplate.DefaultWeapons.Id));
        return EquipmentLoadoutService.Resolve(soldier.Id, element, _doctrine, authored, elementFallback, squadFallback,
            new EquipmentValidationContext
            {
                FactionId = squad.Faction?.Id, SpeciesId = soldier.Template.Species?.Id,
                SoldierTemplateId = soldier.Template.Id, PersonalEquipmentRole = element.PersonalEquipmentRole,
                Strength = soldier.Strength, BaseCapacity = soldier.Template.Species?.BaseCapacity ?? 16f,
                HandGroups = Math.Max(1, functioningHands)
            }, ResolveElementArmor(squad, soldier));
    }

    private EquipmentTemplate ResolveElementArmor(Squad squad, ISoldier soldier)
    {
        ArmorTemplate armor = squad?.SquadTemplate?.Elements
            .FirstOrDefault(candidate => candidate.SoldierTemplate == soldier?.Template)
            ?.DefaultArmor
            ?? squad?.SquadTemplate?.Armor;
        return armor == null
            ? null
            : _catalog?.EquipmentTemplates.GetValueOrDefault(
                EquipmentRulesCatalog.GetArmorEquipmentId(armor.Id));
    }
}
