using Godot;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class SquadScreenController : MainScreenController
{
    private readonly SoldierDossierService _dossierService = new();
    private Squad _squad;
    private SquadScreenView _view;
    private int? _editingSoldierId;

    public event EventHandler CampaignChanged;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<SquadScreenView>("DialogView");
        _view.LoadoutChanged += OnLoadoutChanged;
        _view.ReturnToDoctrinePressed += OnReturnToDoctrine;
        _view.CharacterLoadoutSelected += OnCharacterLoadoutSelected;
        _view.CharacterLoadoutReset += OnCharacterLoadoutReset;
        _view.CharacterCustomizeRequested += OnCharacterCustomizeRequested;
        _view.EquipmentLoadoutSaveRequested += OnEquipmentLoadoutSaved;
        _view.ClosePressed += (_, _) => RequestClose();
    }

    public void SetSquad(Squad squad)
    {
        _squad = squad;
        Refresh();
    }

    private void OnLoadoutChanged(object sender, EventArgs e)
    {
        if (_squad == null) return;
        if (_squad.UsesLoadoutDoctrine)
        {
            LoadoutDoctrineService.Customize(_squad);
        }
        _squad.Loadout = _view.WorkingLoadout.ToList();
        CampaignChanged?.Invoke(this, EventArgs.Empty);
        _view.SetDoctrineState("Custom loadout", true);
    }

    private void OnReturnToDoctrine(object sender, EventArgs e)
    {
        if (_squad == null) return;
        LoadoutDoctrineService.ReturnToDoctrine(_squad);
        CampaignChanged?.Invoke(this, EventArgs.Empty);
        Refresh();
    }

    private void OnCharacterLoadoutSelected(object sender, (int SoldierId, WeaponSet WeaponSet) change)
    {
        ISoldier soldier = FindMember(change.SoldierId);
        if (soldier == null) return;
        CharacterLoadoutService.SetPersonalLoadout(soldier, change.WeaponSet);
        CampaignChanged?.Invoke(this, EventArgs.Empty);
        Refresh();
    }

    private void OnCharacterLoadoutReset(object sender, int soldierId)
    {
        ISoldier soldier = FindMember(soldierId);
        if (soldier == null) return;
        SquadTemplateElement element = FindPersonalElement(soldier);
        EquipmentRulesCatalog catalog = GameDataSingleton.Instance?.GameRulesData?.EquipmentCatalog;
        if (element?.PersonalEquipmentRole != null
            && catalog?.PersonalEquipmentRoles.ContainsKey(element.PersonalEquipmentRole.Id) == true)
        {
            GameDataSingleton.Instance.Sector.PlayerForce.Army.EquipmentLoadoutDoctrine
                .ClearPersonalLoadout(soldier.Id);
            CampaignChanged?.Invoke(this, EventArgs.Empty);
            Refresh();
            return;
        }
        CharacterLoadoutService.ClearPersonalLoadout(soldier);
        CampaignChanged?.Invoke(this, EventArgs.Empty);
        Refresh();
    }

    private void OnCharacterCustomizeRequested(object sender, int soldierId)
    {
        ISoldier soldier = FindMember(soldierId);
        SquadTemplateElement element = FindPersonalElement(soldier);
        EquipmentRulesCatalog catalog = GameDataSingleton.Instance?.GameRulesData?.EquipmentCatalog;
        if (soldier == null || element?.PersonalEquipmentRole == null || catalog == null)
        {
            return;
        }

        PersonalEquipmentRole role = catalog.PersonalEquipmentRoles.GetValueOrDefault(
            element.PersonalEquipmentRole.Id) ?? element.PersonalEquipmentRole;
        EquipmentKitTemplate authoredRoleKit = catalog.EquipmentKits.GetValueOrDefault(role.DefaultKitId);
        EquipmentKitTemplate elementFallbackKit = element.DefaultWeapons == null
            ? null
            : catalog.EquipmentKits.GetValueOrDefault(
                EquipmentRulesCatalog.GetKitId(element.DefaultWeapons.Id));
        EquipmentKitTemplate squadFallbackKit = _squad.SquadTemplate.DefaultWeapons == null
            ? null
            : catalog.EquipmentKits.GetValueOrDefault(
                EquipmentRulesCatalog.GetKitId(_squad.SquadTemplate.DefaultWeapons.Id));
        EquipmentValidationContext context = BuildEquipmentContext(soldier, role);
        EquipmentLoadoutDoctrine doctrine = _squad.Faction?.IsPlayerFaction == true
            ? GameDataSingleton.Instance?.Sector?.PlayerForce?.Army?.EquipmentLoadoutDoctrine
            : null;
        ResolvedEquipmentLoadout resolved = EquipmentLoadoutService.Resolve(
            soldier.Id,
            element,
            doctrine,
            authoredRoleKit,
            elementFallbackKit,
            squadFallbackKit,
            context);
        _editingSoldierId = soldier.Id;
        _view.OpenEquipmentEditor(
            $"{soldier.Template.Name} {soldier.Name}",
            $"{role.Name} · {DescribeEquipmentLoadoutSource(resolved)}. Save a complete personal override or cancel to inherit.",
            catalog,
            resolved.Loadout ?? authoredRoleKit?.ToLoadout() ?? new EquipmentLoadout(),
            context);
    }

    private void OnEquipmentLoadoutSaved(EquipmentLoadout loadout)
    {
        if (!_editingSoldierId.HasValue) return;
        ISoldier soldier = FindMember(_editingSoldierId.Value);
        SquadTemplateElement element = FindPersonalElement(soldier);
        EquipmentRulesCatalog catalog = GameDataSingleton.Instance?.GameRulesData?.EquipmentCatalog;
        if (soldier == null || element?.PersonalEquipmentRole == null || catalog == null) return;

        try
        {
            EquipmentLoadoutService.SetPersonalLoadout(
                GameDataSingleton.Instance.Sector.PlayerForce.Army.EquipmentLoadoutDoctrine,
                soldier.Id,
                loadout,
                BuildEquipmentContext(soldier, element.PersonalEquipmentRole));
            CampaignChanged?.Invoke(this, EventArgs.Empty);
            Refresh();
        }
        catch (ArgumentException exception)
        {
            GD.PushWarning($"Personal equipment loadout was not saved: {exception.Message}");
        }
        finally
        {
            _editingSoldierId = null;
        }
    }

    private ISoldier FindMember(int soldierId) =>
        _squad?.Members.FirstOrDefault(member => member.Id == soldierId);

    private void Refresh()
    {
        if (_squad == null || _view == null) return;
        EffectiveLoadout effective = LoadoutDoctrineService.Resolve(_squad);
        int ableBodied = _squad.Members.Count(member => member.IsCombatEffective);
        string location = _squad.CurrentRegion?.Planet?.Name
            ?? _squad.BoardedLocation?.Fleet?.Planet?.Name
            ?? "No active theater";
        _view.Display(
            _squad.Name,
            $"{_squad.SquadTemplate.Name} · {ableBodied} combat-ready · {location}",
            LoadoutDoctrineService.DescribeSource(effective),
            effective.WeaponSets,
            !_squad.UsesLoadoutDoctrine,
            BuildCharacterRows(),
            ElementLoadoutSections.Build(
                _squad.SquadTemplate,
                // Capacity is THIS element's own able-bodied bodies, not the squad's — a
                // sergeant's individually-equipped slot must not eat into the trooper pool.
                element => _squad.Members.Count(
                    member => member.IsCombatEffective && member.Template == element.SoldierTemplate)));
    }

    // Characters carry kit chosen for the individual, so they get a row each rather than a share
    // of the squad's pooled counts. Ordered the way the squad roster reads: leader, then rank.
    private List<CharacterLoadoutRowData> BuildCharacterRows()
    {
        List<CharacterLoadoutRowData> rows = [];
        if (_squad?.Members == null) return rows;

        foreach (ISoldier soldier in _squad.Members
                     .Where(member => FindPersonalElement(member)?.PersonalEquipmentRole != null)
                     .OrderByDescending(member => member.Template.IsSquadLeader)
                     .ThenByDescending(member => member.Template.Rank)
                     .ThenBy(member => member.Name))
        {
            SquadTemplateElement element = FindPersonalElement(soldier);
            EquipmentRulesCatalog catalog = GameDataSingleton.Instance?.GameRulesData?.EquipmentCatalog;
            PersonalEquipmentRole role = element.PersonalEquipmentRole;
            if (catalog?.PersonalEquipmentRoles.ContainsKey(role.Id) == true
                && catalog.EquipmentKits.TryGetValue(role.DefaultKitId, out EquipmentKitTemplate authoredKit))
            {
                EquipmentLoadoutDoctrine doctrine = GameDataSingleton.Instance.Sector.PlayerForce
                    .Army.EquipmentLoadoutDoctrine;
                EquipmentLoadout loadout = doctrine.TryGetPersonalLoadout(soldier.Id, out EquipmentLoadout personal)
                    ? personal
                    : doctrine.TryGetRoleDefault(role.Id, out EquipmentLoadout roleDefault)
                        ? roleDefault
                        : authoredKit.ToLoadout();
                string source = doctrine.PersonalLoadouts.ContainsKey(soldier.Id)
                    ? "Personal override"
                    : doctrine.RoleDefaults.ContainsKey(role.Id)
                        ? "Chapter role default"
                        : "Authored role kit";
                rows.Add(new CharacterLoadoutRowData(
                    soldier.Id,
                    $"{role.Name} · {soldier.Name}",
                    $"{source} · {DescribeEquipmentLoadout(loadout, BuildEquipmentContext(soldier, role))}",
                    [],
                    null,
                    doctrine.PersonalLoadouts.ContainsKey(soldier.Id)));
                continue;
            }

            EffectiveCharacterLoadout resolved = CharacterLoadoutService.Resolve(soldier);
            rows.Add(new CharacterLoadoutRowData(
                soldier.Id,
                $"{soldier.Template.Name} {soldier.Name}",
                DescribeRow(soldier, resolved),
                element?.GetMenu(CharacterLoadoutService.CommandWeaponGroup) ?? [],
                resolved?.WeaponSet,
                resolved?.Source == CharacterLoadoutSource.Personal));
        }
        return rows;
    }

    // Pairs the loadout's provenance with the brother's gun and blade honors, so the player can
    // judge a weapon choice against his record. Honors rather than skill values on purpose: raw
    // soldier stats are never surfaced. They also stay put as the dropdown changes, since they
    // describe the man and not the weapon currently selected for him.
    private string DescribeRow(ISoldier soldier, EffectiveCharacterLoadout resolved)
    {
        string source = CharacterLoadoutService.DescribeSource(resolved);
        if (soldier is not PlayerSoldier playerSoldier)
        {
            return source;
        }
        IReadOnlyList<string> honors = _dossierService.BuildCombatHonorNames(
            playerSoldier, GameDataSingleton.Instance?.GameRulesData?.AwardCatalog);
        return honors.Count == 0 ? source : $"{source} · {string.Join(" · ", honors)}";
    }

    private SquadTemplateElement FindPersonalElement(ISoldier soldier) =>
        soldier?.AssignedSquad?.SquadTemplate?.Elements
            .FirstOrDefault(element => element.SoldierTemplate == soldier.Template);

    private EquipmentValidationContext BuildEquipmentContext(
        ISoldier soldier,
        PersonalEquipmentRole role) => new()
        {
            FactionId = _squad?.Faction?.Id,
            SpeciesId = soldier?.Template?.Species?.Id,
            SoldierTemplateId = soldier?.Template?.Id,
            PersonalEquipmentRole = role,
            Strength = soldier?.Strength ?? 0,
            HandGroups = 2,
            BaseCapacity = soldier?.Template?.Species?.BaseCapacity ?? 16
        };

    private static string DescribeEquipmentLoadoutSource(ResolvedEquipmentLoadout resolved) =>
        resolved?.Source switch
        {
            EquipmentLoadoutSource.Personal => "Personal override",
            EquipmentLoadoutSource.ChapterRole => "Chapter role default",
            EquipmentLoadoutSource.AuthoredRole => "Authored role kit",
            EquipmentLoadoutSource.ElementFallback => "Element fallback",
            EquipmentLoadoutSource.SquadFallback => "Squad fallback",
            _ => "Inherited equipment"
        };

    private static string DescribeEquipmentLoadout(
        EquipmentLoadout loadout,
        EquipmentValidationContext context)
    {
        if (loadout == null) return "No loadout";
        string armor = loadout.Armor?.Name ?? "No armor";
        string items = string.Join(", ", loadout.Items.Select(item =>
            item.Quantity > 1 ? $"{item.Equipment.Name} ×{item.Quantity}" : item.Equipment.Name));
        return $"{armor} · {(string.IsNullOrEmpty(items) ? "No carried items" : items)} · "
            + $"{EquipmentLoadoutValidator.GetUsedCapacity(loadout):0.##}/"
            + $"{EquipmentLoadoutValidator.GetAvailableCapacity(loadout, context):0.##} load";
    }
}
