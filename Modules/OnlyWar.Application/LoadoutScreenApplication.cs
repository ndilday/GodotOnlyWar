using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Readiness;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Application;

public sealed class LoadoutScreenApplication : CampaignScreenApplication,
    ILoadoutScreenApplication
{
    private const string NoCampaignMessage = "No campaign is active.";
    private const string StaleSessionMessage = "This campaign is no longer active.";

    private readonly SoldierDossierService _dossierService = new();

    public LoadoutScreenApplication(CampaignApplicationContext context) : base(context) { }

    public SquadLoadoutView QuerySquadLoadout(int squadId)
    {
        GameSession session = ActiveSession;
        Squad squad = FindPlayerSquad(squadId);
        if (squad == null) return SquadLoadoutView.Missing;

        PlayerForce force = session.Sector.PlayerForce;
        ChapterOperationalDoctrine doctrine = force?.Army?.ChapterOperationalDoctrine;
        EffectiveLoadout effective = LoadoutDoctrineService.Resolve(squad, force);
        int dutyReady = SquadStrengthSnapshotBuilder
            .Build(squad, program: force?.RecruitmentProgram, doctrine: doctrine)
            .DutyReady;
        string location = squad.CurrentRegion?.Planet?.Name
            ?? squad.BoardedLocation?.Fleet?.Planet?.Name
            ?? "No active theater";

        return new SquadLoadoutView(
            true,
            squad.Name,
            $"{squad.SquadTemplate.Name} · {dutyReady} duty-ready · {location}",
            LoadoutDoctrineService.DescribeSource(effective),
            effective.WeaponSets,
            !squad.UsesLoadoutDoctrine,
            BuildSquadCharacterRows(squad, force),
            ElementLoadoutSections.Build(
                squad.SquadTemplate,
                // Capacity is THIS element's own able-bodied bodies, not the squad's — a
                // sergeant's individually-equipped slot must not eat into the trooper pool.
                element => squad.Members.Count(
                    member => member.Template == element.SoldierTemplate
                        && (member is not PlayerSoldier player || player.IndividualPosting == null)
                        && DutyReadinessService.Evaluate(
                            member,
                            doctrine: doctrine,
                            recruitmentProgram: force?.RecruitmentProgram).IsDutyReady),
                squad.Members.Count));
    }

    public LoadoutCommandResult SetSquadLoadout(
        Guid sessionToken, int squadId, IReadOnlyList<WeaponSet> loadout)
    {
        if (RejectLoadoutCommand(sessionToken) is LoadoutCommandResult rejection) return rejection;
        Squad squad = FindPlayerSquad(squadId);
        if (squad == null) return LoadoutCommandResult.Failed("That squad is no longer available.");

        PlayerForce force = ActiveSession.Sector.PlayerForce;
        if (squad.UsesLoadoutDoctrine)
        {
            LoadoutDoctrineService.Customize(squad, force);
        }
        squad.Loadout = (loadout ?? []).ToList();
        return LoadoutCommandResult.Ok();
    }

    public LoadoutCommandResult ReturnSquadToDoctrine(Guid sessionToken, int squadId)
    {
        if (RejectLoadoutCommand(sessionToken) is LoadoutCommandResult rejection) return rejection;
        Squad squad = FindPlayerSquad(squadId);
        if (squad == null) return LoadoutCommandResult.Failed("That squad is no longer available.");

        LoadoutDoctrineService.ReturnToDoctrine(squad);
        return LoadoutCommandResult.Ok();
    }

    public LoadoutCommandResult SetSquadCharacterWeaponSet(
        Guid sessionToken, int squadId, int soldierId, WeaponSet weaponSet)
    {
        if (RejectLoadoutCommand(sessionToken) is LoadoutCommandResult rejection) return rejection;
        ISoldier soldier = FindSquadMember(squadId, soldierId);
        if (soldier == null) return LoadoutCommandResult.Failed("That brother is no longer here.");

        CharacterLoadoutService.SetPersonalLoadout(
            soldier, weaponSet, ActiveSession.Sector.PlayerForce);
        return LoadoutCommandResult.Ok();
    }

    public LoadoutCommandResult ResetSquadCharacterLoadout(
        Guid sessionToken, int squadId, int soldierId)
    {
        if (RejectLoadoutCommand(sessionToken) is LoadoutCommandResult rejection) return rejection;
        ISoldier soldier = FindSquadMember(squadId, soldierId);
        if (soldier == null) return LoadoutCommandResult.Failed("That brother is no longer here.");

        PlayerForce force = ActiveSession.Sector.PlayerForce;
        SquadTemplateElement element = FindPersonalElement(soldier);
        EquipmentRulesCatalog catalog = ActiveSession.Rules?.EquipmentCatalog;
        // An itemized role clears its personal override in the equipment doctrine; the legacy
        // weapon-set path clears the character loadout instead.
        if (element?.PersonalEquipmentRole != null
            && catalog?.PersonalEquipmentRoles.ContainsKey(element.PersonalEquipmentRole.Id) == true)
        {
            force.Army.EquipmentLoadoutDoctrine.ClearPersonalLoadout(soldier.Id);
            return LoadoutCommandResult.Ok();
        }

        CharacterLoadoutService.ClearPersonalLoadout(soldier, force);
        return LoadoutCommandResult.Ok();
    }

    public EquipmentEditorView QuerySoldierEquipmentEditor(int squadId, int soldierId)
    {
        ISoldier soldier = FindSquadMember(squadId, soldierId);
        Squad squad = FindPlayerSquad(squadId);
        SquadTemplateElement element = FindPersonalElement(soldier);
        EquipmentRulesCatalog catalog = ActiveSession?.Rules?.EquipmentCatalog;
        if (soldier == null || squad == null || element?.PersonalEquipmentRole == null
            || catalog == null)
        {
            return EquipmentEditorView.Unavailable;
        }

        PersonalEquipmentRole role = catalog.PersonalEquipmentRoles.GetValueOrDefault(
            element.PersonalEquipmentRole.Id) ?? element.PersonalEquipmentRole;
        EquipmentKitTemplate authoredRoleKit =
            catalog.EquipmentKits.GetValueOrDefault(role.DefaultKitId);
        EquipmentKitTemplate elementFallbackKit = element.DefaultWeapons == null
            ? null
            : catalog.EquipmentKits.GetValueOrDefault(
                EquipmentRulesCatalog.GetKitId(element.DefaultWeapons.Id));
        EquipmentKitTemplate squadFallbackKit = squad.SquadTemplate.DefaultWeapons == null
            ? null
            : catalog.EquipmentKits.GetValueOrDefault(
                EquipmentRulesCatalog.GetKitId(squad.SquadTemplate.DefaultWeapons.Id));
        EquipmentValidationContext context = BuildSoldierEquipmentContext(squad, soldier, role);
        EquipmentLoadoutDoctrine doctrine = squad.Faction?.IsPlayerFaction == true
            ? ActiveSession.Sector.PlayerForce?.Army?.EquipmentLoadoutDoctrine
            : null;
        ResolvedEquipmentLoadout resolved = EquipmentLoadoutService.Resolve(
            soldier.Id, element, doctrine, authoredRoleKit, elementFallbackKit,
            squadFallbackKit, context);

        return new EquipmentEditorView(
            true,
            $"{soldier.Template.Name} {soldier.Name}",
            $"{role.Name} · {DescribeEquipmentLoadoutSource(resolved)}. "
                + "Save a complete personal override or cancel to inherit.",
            catalog,
            resolved.Loadout ?? authoredRoleKit?.ToLoadout() ?? new EquipmentLoadout(),
            context);
    }

    public LoadoutCommandResult SaveSoldierEquipment(
        Guid sessionToken, int squadId, int soldierId, EquipmentLoadout loadout)
    {
        if (RejectLoadoutCommand(sessionToken) is LoadoutCommandResult rejection) return rejection;
        ISoldier soldier = FindSquadMember(squadId, soldierId);
        Squad squad = FindPlayerSquad(squadId);
        SquadTemplateElement element = FindPersonalElement(soldier);
        EquipmentRulesCatalog catalog = ActiveSession.Rules?.EquipmentCatalog;
        if (soldier == null || squad == null || element?.PersonalEquipmentRole == null
            || catalog == null)
        {
            return LoadoutCommandResult.Failed("That loadout slot is no longer available.");
        }

        try
        {
            EquipmentLoadoutService.SetPersonalLoadout(
                ActiveSession.Sector.PlayerForce.Army.EquipmentLoadoutDoctrine,
                soldier.Id,
                loadout,
                BuildSoldierEquipmentContext(squad, soldier, element.PersonalEquipmentRole));
            return LoadoutCommandResult.Ok();
        }
        catch (ArgumentException exception)
        {
            return LoadoutCommandResult.Failed(
                $"Personal equipment loadout was not saved: {exception.Message}");
        }
    }

    public LoadoutDoctrineScopeView QueryDoctrineScope(int? planetId)
    {
        GameSession session = ActiveSession;
        PlayerForce force = session?.Sector.PlayerForce;
        if (force?.Army == null) return null;
        Planet planet = ResolveDoctrinePlanet(planetId);
        if (planetId.HasValue && planet == null) return null;

        LoadoutDoctrine doctrine = planet?.LoadoutDoctrine ?? force.Army.LoadoutDoctrine;
        List<LoadoutTemplateOption> templates = DoctrineTemplates(force)
            .Select(template => new LoadoutTemplateOption(
                template.Id,
                planet != null && !doctrine.Loadouts.ContainsKey(template.Id)
                    ? $"{template.Name}\nInherits chapter"
                    : template.Name,
                false))
            .ToList();

        return new LoadoutDoctrineScopeView(
            true,
            planet == null ? "Chapter Loadouts" : $"{planet.Name} Theater Loadouts",
            planet == null
                ? "Set the chapter-wide baseline for each squad type. Squads with a theater override or custom loadout are unaffected."
                : "Create only the overrides this theater needs. Unmodified squad types continue to inherit chapter doctrine.",
            "Set the chapter-wide kit for each command and specialist role. "
                + "Individuals equipped from their squad screen keep their personal loadout.",
            "Choose the Chapter's operational standard. Physical incapacity, untreated severance, "
                + "procedure reservations, and fewer than two functioning arms remain unconditional exclusions.",
            planet == null ? "Save Chapter Default" : "Save Theater Override",
            templates);
    }

    public LoadoutTemplateDetailView QueryTemplateLoadout(int? planetId, int templateId)
    {
        PlayerForce force = ActiveSession?.Sector.PlayerForce;
        if (force?.Army == null) return LoadoutTemplateDetailView.Missing;
        Planet planet = ResolveDoctrinePlanet(planetId);
        if (planetId.HasValue && planet == null) return LoadoutTemplateDetailView.Missing;

        SquadTemplate template = DoctrineTemplates(force)
            .FirstOrDefault(candidate => candidate.Id == templateId);
        if (template == null) return LoadoutTemplateDetailView.Missing;

        LoadoutDoctrine doctrine = planet?.LoadoutDoctrine ?? force.Army.LoadoutDoctrine;
        bool hasLocal = doctrine.TryGetLoadout(template.Id, out IReadOnlyList<WeaponSet> loadout);
        if (!hasLocal && planet != null)
        {
            force.Army.LoadoutDoctrine.TryGetLoadout(template.Id, out loadout);
        }
        loadout ??= [];

        return new LoadoutTemplateDetailView(
            true,
            template.Name,
            planet == null
                ? doctrine.Loadouts.ContainsKey(template.Id)
                    ? "Explicit chapter default"
                    : "Template standard; save to establish a chapter default"
                : planet.LoadoutDoctrine.Loadouts.ContainsKey(template.Id)
                    ? "Planetary theater override"
                    : "Inherited from chapter doctrine",
            loadout,
            // No live roster at template scope, so capacity is each element's own MaximumNumber
            // rather than an able-bodied count.
            ElementLoadoutSections.Build(template, element => element.MaximumNumber),
            planet != null && planet.LoadoutDoctrine.Loadouts.ContainsKey(template.Id));
    }

    public LoadoutCommandResult SaveTemplateLoadout(
        Guid sessionToken, int? planetId, int templateId, IReadOnlyList<WeaponSet> loadout)
    {
        if (RejectLoadoutCommand(sessionToken) is LoadoutCommandResult rejection) return rejection;
        PlayerForce force = ActiveSession.Sector.PlayerForce;
        Planet planet = ResolveDoctrinePlanet(planetId);
        if (force?.Army == null || (planetId.HasValue && planet == null))
        {
            return LoadoutCommandResult.Failed("That doctrine scope is no longer available.");
        }
        if (!DoctrineTemplates(force).Any(template => template.Id == templateId))
        {
            return LoadoutCommandResult.Failed("The chapter no longer fields that squad type.");
        }

        (planet?.LoadoutDoctrine ?? force.Army.LoadoutDoctrine)
            .SetLoadout(templateId, loadout ?? []);
        return LoadoutCommandResult.Ok();
    }

    public LoadoutCommandResult InheritTemplateLoadout(
        Guid sessionToken, int planetId, int templateId)
    {
        if (RejectLoadoutCommand(sessionToken) is LoadoutCommandResult rejection) return rejection;
        Planet planet = ResolveDoctrinePlanet(planetId);
        if (planet == null) return LoadoutCommandResult.Failed("That world is no longer charted.");

        return planet.LoadoutDoctrine.RemoveLoadout(templateId)
            ? LoadoutCommandResult.Ok()
            : LoadoutCommandResult.Failed("That squad type already inherits chapter doctrine.");
    }

    // Every personal-equipment role the chapter actually fields, gathered from the order of
    // battle so a chapter without (say) a Judiciar never shows one. Administrative formations are
    // excluded because they never deploy as units. Roles are keyed by the stable role id, not by
    // SoldierTemplate.Id: the same template may be pooled in one formation and personally
    // equipped in another.
    public IReadOnlyList<CharacterLoadoutRowData> QueryCharacterRoles()
    {
        PlayerForce force = ActiveSession?.Sector.PlayerForce;
        if (force?.Army == null) return [];

        EquipmentRulesCatalog catalog = ActiveSession.Rules?.EquipmentCatalog;
        EquipmentLoadoutDoctrine equipmentDoctrine = force.Army.EquipmentLoadoutDoctrine;
        CharacterLoadoutDoctrine doctrine = force.Army.CharacterLoadoutDoctrine;

        return CharacterRoleElements(force).Values
            .OrderBy(element => element.PersonalEquipmentRole.Name, StringComparer.OrdinalIgnoreCase)
            .Select(element =>
            {
                PersonalEquipmentRole role = element.PersonalEquipmentRole;
                if (catalog?.PersonalEquipmentRoles.ContainsKey(role.Id) == true
                    && catalog.EquipmentKits.TryGetValue(
                        role.DefaultKitId, out EquipmentKitTemplate authoredKit))
                {
                    EquipmentLoadout resolvedLoadout = equipmentDoctrine.TryGetRoleDefault(
                        role.Id, out EquipmentLoadout roleLoadout)
                        ? roleLoadout
                        : authoredKit.ToLoadout();
                    string source = equipmentDoctrine.RoleDefaults.ContainsKey(role.Id)
                        ? "Chapter role override"
                        : "Authored role kit";
                    return new CharacterLoadoutRowData(
                        role.Id,
                        role.Name,
                        $"{source} · {DescribeEquipmentLoadout(resolvedLoadout, BuildRoleEquipmentContext(force, element))}",
                        [],
                        null,
                        equipmentDoctrine.RoleDefaults.ContainsKey(role.Id));
                }

                // Compatibility display for a focused fixture that predates the itemized rules
                // tables. Production uses the branch above and the shared editor.
                EffectiveCharacterLoadout resolved =
                    CharacterLoadoutService.ResolveRole(element, force);
                return new CharacterLoadoutRowData(
                    role.Id,
                    role.Name,
                    CharacterLoadoutService.DescribeSource(resolved),
                    element.GetMenu(CharacterLoadoutService.CommandWeaponGroup),
                    resolved?.WeaponSet,
                    doctrine.RoleDefaults.ContainsKey(element.SoldierTemplate.Id));
            })
            .ToList();
    }

    public LoadoutCommandResult SetCharacterRoleWeaponSet(
        Guid sessionToken, int roleId, WeaponSet weaponSet)
    {
        if (RejectLoadoutCommand(sessionToken) is LoadoutCommandResult rejection) return rejection;
        PlayerForce force = ActiveSession.Sector.PlayerForce;
        if (!CharacterRoleElements(force).TryGetValue(roleId, out SquadTemplateElement element))
        {
            return LoadoutCommandResult.Failed("The chapter no longer fields that role.");
        }

        CharacterLoadoutService.SetRoleDefault(element, weaponSet, force);
        return LoadoutCommandResult.Ok();
    }

    public LoadoutCommandResult ResetCharacterRole(Guid sessionToken, int roleId)
    {
        if (RejectLoadoutCommand(sessionToken) is LoadoutCommandResult rejection) return rejection;
        PlayerForce force = ActiveSession.Sector.PlayerForce;
        EquipmentRulesCatalog catalog = ActiveSession.Rules?.EquipmentCatalog;
        if (catalog?.PersonalEquipmentRoles.ContainsKey(roleId) == true)
        {
            force.Army.EquipmentLoadoutDoctrine.ClearRoleDefault(roleId);
            return LoadoutCommandResult.Ok();
        }

        int templateId = CharacterRoleElements(force)
            .TryGetValue(roleId, out SquadTemplateElement element)
            ? element.SoldierTemplate.Id
            : roleId;
        return force.Army.CharacterLoadoutDoctrine.ClearRoleDefault(templateId)
            ? LoadoutCommandResult.Ok()
            : LoadoutCommandResult.Failed("That role already follows its authored kit.");
    }

    public EquipmentEditorView QueryRoleEquipmentEditor(int roleId)
    {
        PlayerForce force = ActiveSession?.Sector.PlayerForce;
        EquipmentRulesCatalog catalog = ActiveSession?.Rules?.EquipmentCatalog;
        if (force?.Army == null || catalog == null
            || !CharacterRoleElements(force).TryGetValue(roleId, out SquadTemplateElement element)
            || !catalog.PersonalEquipmentRoles.TryGetValue(roleId, out PersonalEquipmentRole role)
            || !catalog.EquipmentKits.TryGetValue(
                role.DefaultKitId, out EquipmentKitTemplate authoredKit))
        {
            return EquipmentEditorView.Unavailable;
        }

        EquipmentLoadoutDoctrine doctrine = force.Army.EquipmentLoadoutDoctrine;
        EquipmentLoadout loadout = doctrine.TryGetRoleDefault(roleId, out EquipmentLoadout stored)
            ? stored
            : authoredKit.ToLoadout();
        return new EquipmentEditorView(
            true,
            $"{role.Name} equipment",
            "Save a complete role default. Individual soldiers may later save their own complete personal override.",
            catalog,
            loadout,
            BuildRoleEquipmentContext(force, element));
    }

    public LoadoutCommandResult SaveRoleEquipment(
        Guid sessionToken, int roleId, EquipmentLoadout loadout)
    {
        if (RejectLoadoutCommand(sessionToken) is LoadoutCommandResult rejection) return rejection;
        PlayerForce force = ActiveSession.Sector.PlayerForce;
        if (force?.Army == null
            || !CharacterRoleElements(force).TryGetValue(roleId, out SquadTemplateElement element))
        {
            return LoadoutCommandResult.Failed("The chapter no longer fields that role.");
        }

        try
        {
            EquipmentLoadoutService.SetRoleDefault(
                force.Army.EquipmentLoadoutDoctrine,
                element.PersonalEquipmentRole,
                loadout,
                BuildRoleEquipmentContext(force, element));
            return LoadoutCommandResult.Ok();
        }
        catch (ArgumentException exception)
        {
            return LoadoutCommandResult.Failed(
                $"Equipment role loadout was not saved: {exception.Message}");
        }
    }

    public OperationalDoctrineView QueryOperationalDoctrine()
    {
        ChapterOperationalDoctrine doctrine =
            ActiveSession?.Sector.PlayerForce?.Army?.ChapterOperationalDoctrine
            ?? new ChapterOperationalDoctrine();
        return new OperationalDoctrineView(
            InjuryThresholdLabels,
            IndexOfThreshold(doctrine.InjuryThreshold),
            doctrine.RequireDutyReadySquadLeader,
            doctrine.MinimumDutyReadySquadStrength);
    }

    public string DescribeOperationalDoctrineConsequence(
        int injuryThresholdIndex, bool requireDutyReadySquadLeader, int minimumStrength)
    {
        PlayerForce force = ActiveSession?.Sector.PlayerForce;
        if (force?.Army == null) return string.Empty;

        ChapterOperationalDoctrine staged = StageDoctrine(
            injuryThresholdIndex, requireDutyReadySquadLeader, minimumStrength);
        RecruitmentProgram recruitment = force.RecruitmentProgram;
        List<Squad> squads = force.Army.OrderOfBattle?.GetAllSquads()
            .Where(squad => squad?.Faction?.IsPlayerFaction == true
                && squad.IsPresentOperationalForce)
            .ToList() ?? [];
        int withheld = squads.Sum(squad =>
            SquadStrengthSnapshotBuilder.Build(squad, program: recruitment, doctrine: staged)
                .DoctrineWithholdingCount);
        int unable = squads.Count(squad =>
            SquadReadinessService.Evaluate(squad, program: recruitment, doctrine: staged)
                .StructuralBlockers.Count > 0);
        return $"Current roster consequence: {withheld} soldier(s) withheld by injury doctrine; "
            + $"{unable} squad(s) unable to deploy under these structural rules."
            + "\nOperational fractions use duty-ready members and the leader counts toward the minimum.";
    }

    public LoadoutCommandResult SaveOperationalDoctrine(
        Guid sessionToken,
        int injuryThresholdIndex,
        bool requireDutyReadySquadLeader,
        int minimumStrength)
    {
        if (RejectLoadoutCommand(sessionToken) is LoadoutCommandResult rejection) return rejection;
        ChapterOperationalDoctrine live =
            ActiveSession.Sector.PlayerForce?.Army?.ChapterOperationalDoctrine;
        if (live == null) return LoadoutCommandResult.Failed("No chapter doctrine is loaded.");

        live.ReplaceWith(StageDoctrine(
            injuryThresholdIndex, requireDutyReadySquadLeader, minimumStrength));
        return LoadoutCommandResult.Ok();
    }

    private static readonly IReadOnlyList<string> InjuryThresholdLabels =
        ChapterOperationalDoctrine.InjuryThresholdOptions
            .Select(ChapterOperationalDoctrine.DescribeThreshold)
            .ToList();

    private static int IndexOfThreshold(WoundLevel? threshold)
    {
        int index = ChapterOperationalDoctrine.InjuryThresholdOptions
            .ToList()
            .FindIndex(candidate => candidate == threshold);
        return index < 0 ? 0 : index;
    }

    private static ChapterOperationalDoctrine StageDoctrine(
        int injuryThresholdIndex, bool requireDutyReadySquadLeader, int minimumStrength)
    {
        int index = Math.Clamp(
            injuryThresholdIndex, 0, ChapterOperationalDoctrine.InjuryThresholdOptions.Count - 1);
        return new ChapterOperationalDoctrine
        {
            InjuryThreshold = ChapterOperationalDoctrine.InjuryThresholdOptions[index],
            RequireDutyReadySquadLeader = requireDutyReadySquadLeader,
            MinimumDutyReadySquadStrength = minimumStrength
        };
    }

    private LoadoutCommandResult RejectLoadoutCommand(Guid sessionToken)
    {
        if (ActiveSession == null) return LoadoutCommandResult.Failed(NoCampaignMessage);
        if (sessionToken != SessionToken) return LoadoutCommandResult.Failed(StaleSessionMessage);
        return null;
    }

    private Planet ResolveDoctrinePlanet(int? planetId) =>
        planetId.HasValue
            && ActiveSession != null
            && ActiveSession.Sector.Planets.TryGetValue(planetId.Value, out Planet planet)
                ? planet
                : null;

    // A squad type belongs here if it has any pooled group to spend, which is every group other
    // than Command Weapon — not every group with a maximum above 1. Tactical Squad's two groups
    // are both (0,1) and it is still very much a squad type worth a doctrine.
    private static List<SquadTemplate> DoctrineTemplates(PlayerForce force) =>
        force?.Army?.OrderOfBattle?.GetAllSquads()
            .Where(squad => squad.IsPresentOperationalForce
                && squad.SquadTemplate.Elements.Any(
                    element => element.PersonalEquipmentRole == null
                        && element.Quotas.Count > 0))
            .Select(squad => squad.SquadTemplate)
            .GroupBy(template => template.Id)
            .Select(group => group.First())
            .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

    private static Dictionary<int, SquadTemplateElement> CharacterRoleElements(PlayerForce force) =>
        force?.Army?.OrderOfBattle?.GetAllSquads()
            .Where(squad => squad.IsPresentOperationalForce)
            .SelectMany(squad => squad.SquadTemplate.Elements)
            .Where(element => element.PersonalEquipmentRole != null)
            .GroupBy(element => element.PersonalEquipmentRole.Id)
            .ToDictionary(group => group.Key, group => group.First())
            ?? [];

    private Squad FindPlayerSquad(int squadId) =>
        ActiveSession?.Sector.PlayerForce?.Army?.OrderOfBattle?.GetAllSquads()
            .FirstOrDefault(squad => squad.Id == squadId);

    private ISoldier FindSquadMember(int squadId, int soldierId) =>
        FindPlayerSquad(squadId)?.Members.FirstOrDefault(member => member.Id == soldierId);

    private static SquadTemplateElement FindPersonalElement(ISoldier soldier) =>
        soldier?.AssignedSquad?.SquadTemplate?.Elements
            .FirstOrDefault(element => element.SoldierTemplate == soldier.Template);

    // Characters carry kit chosen for the individual, so they get a row each rather than a share
    // of the squad's pooled counts. Ordered the way the squad roster reads: leader, then rank.
    private List<CharacterLoadoutRowData> BuildSquadCharacterRows(Squad squad, PlayerForce force)
    {
        List<CharacterLoadoutRowData> rows = [];
        if (squad?.Members == null) return rows;
        EquipmentRulesCatalog catalog = ActiveSession?.Rules?.EquipmentCatalog;

        foreach (ISoldier soldier in squad.Members
                     .Where(member => FindPersonalElement(member)?.PersonalEquipmentRole != null)
                     .OrderByDescending(member => member.Template.IsSquadLeader)
                     .ThenByDescending(member => member.Template.Rank)
                     .ThenBy(member => member.Name))
        {
            SquadTemplateElement element = FindPersonalElement(soldier);
            PersonalEquipmentRole role = element.PersonalEquipmentRole;
            if (catalog?.PersonalEquipmentRoles.ContainsKey(role.Id) == true
                && catalog.EquipmentKits.TryGetValue(
                    role.DefaultKitId, out EquipmentKitTemplate authoredKit))
            {
                EquipmentLoadoutDoctrine doctrine = force.Army.EquipmentLoadoutDoctrine;
                EquipmentLoadout loadout = doctrine.TryGetPersonalLoadout(
                    soldier.Id, out EquipmentLoadout personal)
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
                    $"{source} · {DescribeEquipmentLoadout(loadout, BuildSoldierEquipmentContext(squad, soldier, role))}",
                    [],
                    null,
                    doctrine.PersonalLoadouts.ContainsKey(soldier.Id)));
                continue;
            }

            EffectiveCharacterLoadout resolved = CharacterLoadoutService.Resolve(soldier, force);
            rows.Add(new CharacterLoadoutRowData(
                soldier.Id,
                $"{soldier.Template.Name} {soldier.Name}",
                DescribeCharacterRow(soldier, resolved),
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
    private string DescribeCharacterRow(ISoldier soldier, EffectiveCharacterLoadout resolved)
    {
        string source = CharacterLoadoutService.DescribeSource(resolved);
        if (soldier is not PlayerSoldier playerSoldier)
        {
            return source;
        }

        IReadOnlyList<string> honors = _dossierService.BuildCombatHonorNames(
            playerSoldier, ActiveSession?.Rules?.AwardCatalog);
        return honors.Count == 0 ? source : $"{source} · {string.Join(" · ", honors)}";
    }

    private static EquipmentValidationContext BuildSoldierEquipmentContext(
        Squad squad, ISoldier soldier, PersonalEquipmentRole role) => new()
        {
            FactionId = squad?.Faction?.Id,
            SpeciesId = soldier?.Template?.Species?.Id,
            SoldierTemplateId = soldier?.Template?.Id,
            PersonalEquipmentRole = role,
            Strength = soldier?.Strength ?? 0,
            HandGroups = 2,
            BaseCapacity = soldier?.Template?.Species?.BaseCapacity ?? 16
        };

    private static EquipmentValidationContext BuildRoleEquipmentContext(
        PlayerForce force, SquadTemplateElement element) => new()
        {
            FactionId = force?.Faction?.Id,
            SpeciesId = element?.SoldierTemplate?.Species?.Id,
            SoldierTemplateId = element?.SoldierTemplate?.Id,
            PersonalEquipmentRole = element?.PersonalEquipmentRole,
            Strength = element?.SoldierTemplate?.Species?.Strength?.BaseValue ?? 0,
            HandGroups = 2,
            BaseCapacity = element?.SoldierTemplate?.Species?.BaseCapacity ?? 16
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
        EquipmentLoadout loadout, EquipmentValidationContext context)
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
